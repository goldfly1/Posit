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

            // If model returned JSON, extract the code field
            if (text.StartsWith('{'))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("code", out var codeProp))
                        text = codeProp.GetString() ?? text;
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

        // Rules
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Write a complete Main(string[] args) method.");
        var entryType = comp.EntryType ?? "file";
        if (entryType.Equals("stdin", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine("2. Entry is stdin: use Console.ReadLine() to read input. Do NOT use args[0].");
        else
            sb.AppendLine("2. Entry is a file path: use args[0] as the file path/input. If stdin specified, use Console.ReadLine().");
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