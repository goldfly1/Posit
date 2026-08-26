namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;

/// <summary>
/// Dedicated Wire.cs fixer. Like a plumber — doesn't redesign
/// the building, just fixes the leaking pipe. Gets ONLY the Wire.cs content
/// and the compile errors. No architecture context, no connections, no method
/// signatures. Just "fix these errors in this file."
///
/// This is what a human does: read the compiler error, open the file, fix
/// the specific line, recompile. Iterate until clean.
/// </summary>
public sealed class WireFixer
{
    private readonly IModelGateway _model;
    private readonly WikiSearcher _wiki;

    public WireFixer(IModelGateway model) { _model = model; _wiki = new WikiSearcher(new HttpClient()); }

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
        var systemPrompt = BuildSystemPrompt();

        // Wiki search: find relevant examples for the compile errors
        var wikiExamples = "";
        if (compileErrors.Length > 0)
        {
            var errorQuery = string.Join(" ", compileErrors.Take(3).Select(e => e.Length > 100 ? e[..100] : e));
            wikiExamples = await _wiki.SearchAsync(errorQuery, limit: 2, ct);
            if (!string.IsNullOrWhiteSpace(wikiExamples))
                Console.Error.WriteLine("[wire-fixer] wiki search returned examples");
        }

        var userPrompt = BuildUserContent(wireCsContent, compileErrors, translatedCSharpTypes, wikiExamples);

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
    /// System prompt: role + C# type reference. Short — the model reads this.
    /// </summary>
    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Senior C# Developer. The file Wire.cs has compile errors.");
        sb.AppendLine("Fix ONLY the errors listed in the user message. Keep everything else unchanged.");
        sb.AppendLine("Output the complete fixed Wire.cs file — ONLY C# code, no explanations, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: The user message contains the ACTUAL C# type definitions from the interface.");
        sb.AppendLine("Use the EXACT property names from the type definitions. Do NOT guess.");
        sb.AppendLine();
        sb.AppendLine("═══ NATIVE C# TYPE CONVERSIONS ═══");
        sb.AppendLine("  string -> int: int.Parse(s)");
        sb.AppendLine("  string -> double: double.Parse(s)");
        sb.AppendLine("  string -> bool: bool.Parse(s)");
        sb.AppendLine("  string[] -> string: string.Join(\"\\n\", arr)");
        sb.AppendLine("  string -> string[]: s.Split('\\n')");
        sb.AppendLine("  List<string> -> string[]: list.ToArray()");
        sb.AppendLine("  string[] -> List<string>: arr.ToList()");
        return sb.ToString();
    }

    /// <summary>
    /// User content: errors + type definitions + Wire.cs. This goes in the USER message
    /// where the model's attention actually goes (not the system message).
    /// </summary>
    private static string BuildUserContent(string wireCsContent, string[] compileErrors, string? translatedCSharpTypes, string wikiExamples = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ PROBLEMS TO FIX ═══");
        foreach (var err in compileErrors)
            sb.AppendLine($"  {err}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(translatedCSharpTypes))
        {
            sb.AppendLine("═══ C# TYPE DEFINITIONS (use THESE exact property names) ═══");
            sb.AppendLine(translatedCSharpTypes);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(wikiExamples))
        {
            sb.AppendLine(wikiExamples);
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