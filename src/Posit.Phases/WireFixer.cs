namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;

/// <summary>
/// Dedicated Wire.cs fixer. Like a plumber — doesn't redesign
/// the building, just fixes the leaking pipe. Gets ONLY the Wire.cs content
/// and the compile errors, with the ISequence API reference. No architecture
/// context, no connections, no method signatures. Just "fix these errors in
/// this file."
///
/// This is what a human does: read the compiler error, open the file, fix
/// the specific line, recompile. Iterate until clean.
/// </summary>
public sealed class WireFixer
{
    private readonly IModelGateway _model;

    public WireFixer(IModelGateway model) => _model = model;

    /// <summary>
    /// Fix compile errors in Wire.cs. Returns the fixed C# code, or null if
    /// the model couldn't fix it.
    /// </summary>
    public async Task<string?> FixAsync(
        string wireCsContent,
        string[] compileErrors,
        PhaseContext context,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildFixerPrompt(wireCsContent, compileErrors);

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "Fixed C# source code only (complete Wire.cs file)",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1, // low temperature — this is a targeted fix, not creative work
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

            // The fixer should return raw C# — but handle JSON wrapping just in case
            var jsonStart = text.IndexOf('{');
            if (jsonStart >= 0)
            {
                try
                {
                    var jsonText = text[jsonStart..];
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                    foreach (var fieldName in new[] { "code", "wireCode", "wire", "wireCs", "source", "content",
                        "file", "output", "result", "main", "fixed_file", "fixedFile", "answer", "solution", "cs" })
                    {
                        if (doc.RootElement.TryGetProperty(fieldName, out var prop)
                            && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            text = prop.GetString() ?? text;
                            break;
                        }
                    }
                }
                catch { }
            }

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:csharp|cs)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
                return fenceMatch.Groups[1].Value.Trim();

            return text;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[wire-fixer] Model call failed: {ex.Message}");
            return null;
        }
    }

    private static string BuildFixerPrompt(string wireCsContent, string[] compileErrors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a C# code fixer. The file Wire.cs has problems.");
        sb.AppendLine("Fix ONLY the problems listed below. Keep everything else unchanged.");
        sb.AppendLine("Output the complete fixed Wire.cs file.");
        sb.AppendLine();

        sb.AppendLine("═══ PROBLEMS TO FIX ═══");
        foreach (var err in compileErrors)
            sb.AppendLine($"  {err}");
        sb.AppendLine();

        sb.AppendLine("═══ DAFNY RUNTIME API — ISequence<T> interface ═══");
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
        sb.AppendLine("  Type conversions:");
        sb.AppendLine("    string → ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)");
        sb.AppendLine("    ISequence<Rune> → string: new string(seq.Select(r => (char)r.Value).ToArray())");
        sb.AppendLine("    ISequence<ISequence<Rune>> → string: string.Join(\"\\n\", seq.Select(row => string.Join(\" \", row.Select(r => (char)r.Value))))");
        sb.AppendLine("    ISequence<ISequence<ISequence<Rune>>> → string: string.Join(\"\\n\", seq.Select(row => string.Join(\" \", row.Select(field => new string(field.Select(r => (char)r.Value).ToArray())))))");
        sb.AppendLine("    string[] → ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(string.Join(\"\\n\", arr))");
        sb.AppendLine("    string[] → ISequence<ISequence<Rune>>: Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromArray(arr.Select(s => Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)).ToArray())");
        sb.AppendLine();

        sb.AppendLine("═══ WIRE.CS TO FIX ═══");
        sb.AppendLine(wireCsContent);
        sb.AppendLine();
        sb.AppendLine("═══ END ═══");
        sb.AppendLine("Output the complete fixed Wire.cs file. ONLY C# code, no explanations, no markdown fences.");

        return sb.ToString();
    }
}