namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;

/// <summary>
/// Model-based wiring generator. Asks the LLM to write Wire.cs from the
/// connection list and method signatures. The Docker build verifies it compiles.
/// The bot harness verifies it works. No rules — the model writes the glue code.
/// </summary>
public sealed class ModelWiringGenerator
{
    private readonly IModelGateway _model;

    public ModelWiringGenerator(IModelGateway model) => _model = model;

    public async Task<string?> GenerateAsync(
        Component comp,
        ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translatedSigs,
        Dictionary<string, List<CsMethodSignature>> stubSigs,
        PhaseContext context,
        CancellationToken ct = default)
    {
        // Build the prompt with connections, signatures, and type conversion reference
        var systemPrompt = BuildPrompt(comp, contract, translatedSigs, stubSigs);

        // Inject correction signal if present (e.g. Docker build errors from previous attempt)
        if (context.CorrectionSignal is { Length: > 0 })
        {
            var sb2 = new StringBuilder(systemPrompt);
            sb2.AppendLine();
            sb2.AppendLine("═══ CORRECTION SIGNAL — your previous Wire.cs had these compile errors ═══");
            sb2.AppendLine("Fix ALL of the following before resubmitting:");
            foreach (var signal in context.CorrectionSignal)
                sb2.AppendLine($"  {signal}");
            sb2.AppendLine("═══ END CORRECTION SIGNAL ═══");
            systemPrompt = sb2.ToString();
        }

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "C# source code only (Wire.cs content)",
            ModelTier = ModelTier.Fast,
            Temperature = 0.2,
            MaxOutputTokens = 4096,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "WireCs",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;

            var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim();

            // If model returned JSON, extract the code field.
            // The model wraps C# code in JSON with varying field names.
            // Try to find JSON in the text (might have leading whitespace or tags).
            var jsonStart = text.IndexOf('{');
            if (jsonStart >= 0)
            {
                try
                {
                    var jsonText = text[jsonStart..];
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;
                    string? extracted = null;
                    // Try known field names in order of likelihood
                    foreach (var fieldName in new[] { "code", "wireCode", "wire", "wireCs", "source", "content",
                        "file", "output", "result", "main", "fixed_file", "fixedFile", "answer", "solution", "cs" })
                    {
                        if (root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            extracted = prop.GetString();
                            break;
                        }
                    }
                    // Fallback: find the first string property that looks like C# code
                    if (extracted is null)
                    {
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                                && (prop.Value.GetString()?.Contains("class") == true
                                    || prop.Value.GetString()?.Contains("static") == true
                                    || prop.Value.GetString()?.Contains("void") == true))
                            {
                                extracted = prop.Value.GetString();
                                break;
                            }
                        }
                    }
                    if (extracted is not null)
                        text = extracted;
                }
                catch { }
            }

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:csharp|cs)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
                return fenceMatch.Groups[1].Value.Trim();

            // If the model returned a full file (has class Wire), use it as-is
            if (text.Contains("class Wire") || text.Contains("static class Wire"))
            {
                // Ensure it has the required using statements
                if (!text.Contains("using System.Numerics"))
                    text = "using System.Numerics;\n" + text;
                if (!text.Contains("using Dafny"))
                    text = "using Dafny;\n" + text;
                return text;
            }

            // Otherwise wrap in boilerplate
            return $@"using System;
using System.Linq;
using System.Numerics;
using Dafny;

namespace {comp.Name}
{{
    public static class Wire
    {{
{text}
    }}
}}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[wiring] Model generation failed: {ex.Message}");
            return null;
        }
    }

    private static string BuildPrompt(
        Component comp,
        ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translatedSigs,
        Dictionary<string, List<CsMethodSignature>> stubSigs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are writing Wire.cs — the C# entry point that wires together Dafny components.");
        sb.AppendLine("Write a complete C# Main method that calls the components in order, passing results between them.");
        sb.AppendLine();
        sb.AppendLine($"Component: {comp.Name}");
        sb.AppendLine($"Spec: {comp.Responsibility}");
        if (!string.IsNullOrWhiteSpace(comp.EntryType))
            sb.AppendLine($"Entry type: {comp.EntryType} (file=args[0] path, stdin=Console.ReadLine)");
        if (!string.IsNullOrWhiteSpace(comp.BranchCondition))
            sb.AppendLine($"Branch condition: {comp.BranchCondition}");
        sb.AppendLine();

        // List connections
        sb.AppendLine("Connections (call in this order):");
        for (int i = 0; i < comp.Connections.Length; i++)
        {
            var conn = comp.Connections[i];
            sb.AppendLine($"  {i}. {conn.FromMethod} -> {conn.ToComponent}.{conn.ToMethod}");
            if (conn.ArgMappings.Length > 0)
                sb.AppendLine($"     argMappings: {string.Join(", ", conn.ArgMappings)}");
        }
        sb.AppendLine();

        // List method signatures for each target component
        sb.AppendLine("Method signatures (ACTUAL C# types from translated Dafny):");
        foreach (var conn in comp.Connections)
        {
            var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
            if (targetComp == null) continue;

            var sigs = GetSigs(targetComp, translatedSigs, stubSigs);
            if (sigs == null || sigs.Count == 0) continue;

            var sig = sigs.FirstOrDefault(s => s.Name == conn.ToMethod)
                     ?? sigs.FirstOrDefault(s => s.Name.Contains(conn.ToMethod) || conn.ToMethod.Contains(s.Name))
                     ?? sigs[0];

            var targetClass = ResolveTargetClass(targetComp, translatedSigs, stubSigs);
            var paramList = string.Join(", ", sig.ParamTypes.Zip(sig.ParamNames, (t, n) => $"{t} {n}"));
            var retType = sig.ReturnType == "void" ? "void" : sig.ReturnType;
            var outParams = sig.OutParamTypes.Length > 0
                ? ", " + string.Join(", ", sig.OutParamTypes.Select((t, i) => $"out {t} out_{i}"))
                : "";
            sb.AppendLine($"  {targetClass}.{sig.Name}({paramList}{outParams}) -> {retType}");
        }
        sb.AppendLine();

        // Type conversion reference
        sb.AppendLine("TYPE CONVERSIONS (Dafny <-> C#):");
        sb.AppendLine("  string -> ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)");
        sb.AppendLine("  ISequence<Rune> -> string: new string(seq.Select(r => (char)r.Value).ToArray())");
        sb.AppendLine("  ISequence<ISequence<Rune>> -> string: string.Join(\"\\n\", seq.Select(row => string.Join(\" \", row.Select(r => (char)r.Value))))");
        sb.AppendLine("  ISequence<ISequence<ISequence<Rune>>> -> string: string.Join(\"\\n\", seq.Select(row => string.Join(\" \", row.Select(field => new string(field.Select(r => (char)r.Value).ToArray())))))");
        sb.AppendLine("  string[] -> ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(string.Join(\"\\n\", arr))");
        sb.AppendLine("  string[] -> ISequence<ISequence<Rune>>: Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromArray(arr.Select(s => Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)).ToArray())");
        sb.AppendLine("  int/BigInteger: use BigInteger.Zero for default, BigInteger for arithmetic");
        sb.AppendLine();
        sb.AppendLine("CRITICAL DAFNY RUNTIME API — ISequence<T> interface:");
        sb.AppendLine("  ISequence<T> is the C# type for Dafny seq<T>. It implements IEnumerable<T>.");
        sb.AppendLine("  REAL API (from DafnyRuntime source):");
        sb.AppendLine("    .Count           — int property for length (NOT .Length, NOT .Count())");
        sb.AppendLine("    .Select(i)       — element at index i (NOT seq[i] — Select IS the indexer!)");
        sb.AppendLine("    .CloneAsArray()  — returns T[] copy");
        sb.AppendLine("    .Contains(g)     — bool membership check");
        sb.AppendLine("    .Take(n)/.Drop(n) — subsequence operations");
        sb.AppendLine("  Since ISequence<T> implements IEnumerable<T>, LINQ works too:");
        sb.AppendLine("    .Select(r => (char)r.Value)  — LINQ projection (note: same name as indexer, different signature)");
        sb.AppendLine("    .Count()                      — LINQ count (method call, not property)");
        sb.AppendLine("    .ElementAt(i)                 — LINQ indexer");
        sb.AppendLine("  For ISequence<ISequence<T>> (2D): unwrap with .Select(row => ...) first.");
        sb.AppendLine("  Type conversions: string→ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)");
        sb.AppendLine("                     ISequence<Rune>→string: new string(seq.Select(r => (char)r.Value).ToArray())");
        sb.AppendLine();

        // Rules
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Write a complete Main(string[] args) method.");
        var entryType = comp.EntryType ?? "file";
        if (entryType.Equals("stdin", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("2. Entry is STDIN: use Console.ReadLine() to read input. Do NOT use args[0].");
            sb.AppendLine("   Do NOT check args.Length or print usage for missing args — stdin programs don't take file args.");
            sb.AppendLine("   If Console.ReadLine() returns null, print an error and return 1.");
        }
        else
            sb.AppendLine("2. Entry is a file path: use args[0] as the file path/input.");
        sb.AppendLine("3. Call each method in connection order, passing the previous result to the next.");
        sb.AppendLine("4. For void methods with out params: declare out variables, use the first as the chained result.");
        sb.AppendLine("5. If an out param is bool (isValid): check it — if false, print error to stderr and return 1.");
        if (!string.IsNullOrWhiteSpace(comp.BranchCondition))
            sb.AppendLine($"5a. Branch condition from architect: {comp.BranchCondition}. Implement this branching.");
        sb.AppendLine("6. The last call should print the result to stdout. Print the VALUE, not just a unit/type name.");
        sb.AppendLine("7. Apply type conversions at Dafny/io-shell boundaries (see table above).");
        sb.AppendLine("8. Output ONLY the Main method body (no class/namespace wrapper).");
        sb.AppendLine("9. If args.Length == 0, print usage and return 1.");

        return sb.ToString();
    }

    private static List<CsMethodSignature>? GetSigs(
        Component comp,
        Dictionary<string, List<CsMethodSignature>> translated,
        Dictionary<string, List<CsMethodSignature>> stubs) =>
        translated.TryGetValue(comp.Name, out var t) ? t
            : (stubs.TryGetValue(comp.Name, out var s) ? s : null);

    private static string ResolveTargetClass(
        Component targetComp,
        Dictionary<string, List<CsMethodSignature>> translated,
        Dictionary<string, List<CsMethodSignature>> stubs)
    {
        if (targetComp.Classification == ModuleClassification.IoShell &&
            stubs.TryGetValue(targetComp.Name, out var stubMethods) && stubMethods.Count > 0)
        {
            var m = stubMethods[0];
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return m.ClassName;
        }
        if (translated.TryGetValue(targetComp.Name, out var transMethods) && transMethods.Count > 0)
        {
            var m = transMethods[0];
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return m.ClassName;
        }
        return targetComp.Classification == ModuleClassification.IoShell
            ? targetComp.Name : "_module_" + targetComp.Name + ".__default";
    }
}