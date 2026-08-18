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
        string? translatedCSharpTypes,
        PhaseContext context,
        CancellationToken ct = default)
    {
        // Split: system prompt = role + ISequence API (short, model reads this)
        //         user prompt  = errors + type definitions + Wire.cs (where attention goes)
        // The gateway sends SystemPrompt as system message and UserRequest as user message.
        // Previously everything was in SystemPrompt and the user message was generic boilerplate
        // — the model ignored the type definitions because they were in the system message.
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserContent(wireCsContent, compileErrors, translatedCSharpTypes);

        // Inject the actual content into the user message via UserRequest
        context = context with { UserRequest = userPrompt };

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "Fixed C# source code only (complete Wire.cs file)",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1,
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

            // The model wraps C# in JSON despite being told "raw code only."
            // Don't enumerate field names (whack-a-mole). Instead, universally
            // extract: find ANY string property that looks like C# code.
            if (text.TrimStart().StartsWith('{'))
            {
                try
                {
                    var jsonText = text.TrimStart()[text.TrimStart().IndexOf('{')..];
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = prop.Value.GetString();
                            if (s != null && (s.Contains("class ") || s.Contains("static ") || s.Contains("void ")))
                            {
                                text = s;
                                break;
                            }
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

    /// <summary>
    /// System prompt: role + ISequence API reference. Short — the model reads this.
    /// </summary>
    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a C# code fixer. The file Wire.cs has compile errors.");
        sb.AppendLine("Fix ONLY the errors listed in the user message. Keep everything else unchanged.");
        sb.AppendLine("Output the complete fixed Wire.cs file — ONLY C# code, no explanations, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: The user message contains the ACTUAL translated C# type definitions.");
        sb.AppendLine("Dafny-translated types use 'dtor_' prefix on properties (e.g. dtor_isValid, dtor_value).");
        sb.AppendLine("Use the EXACT property names from the type definitions. Do NOT guess.");
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
        return sb.ToString();
    }

    /// <summary>
    /// User content: errors + type definitions + Wire.cs. This goes in the USER message
    /// where the model's attention actually goes (not the system message).
    /// </summary>
    private static string BuildUserContent(string wireCsContent, string[] compileErrors, string? translatedCSharpTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ PROBLEMS TO FIX ═══");
        foreach (var err in compileErrors)
            sb.AppendLine($"  {err}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(translatedCSharpTypes))
        {
            sb.AppendLine("═══ TRANSLATED C# TYPE DEFINITIONS (use THESE exact property names — note the dtor_ prefix!) ═══");
            sb.AppendLine(translatedCSharpTypes);
            sb.AppendLine();
        }

        sb.AppendLine("═══ WIRE.CS TO FIX ═══");
        sb.AppendLine(wireCsContent);
        sb.AppendLine();
        sb.AppendLine("═══ END ═══");
        sb.AppendLine("Output the complete fixed Wire.cs file. ONLY C# code, no explanations, no markdown fences.");

        return sb.ToString();
    }
}