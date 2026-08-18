namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;
using Posit.Contracts.Artifacts;
using Posit.Tools;

/// <summary>
/// Dedicated Dafny logic fixer for "cotton candy" failures: Dafny compiles
/// and Z3 verifies, but the program produces wrong output (e.g. tokenizes
/// characters instead of words).
///
/// Like the WireFixer, this is a specialist — a plumber. It gets ONLY:
///   1. The failing test cases (expected vs actual output)
///   2. The Dafny source code that produced them
///   3. The component's spec/responsibility + test case descriptions
///
/// It does NOT get architecture context, connections, or other components.
/// It fixes the Dafny logic, Z3 re-verifies, and if verification passes,
/// translates to C# and returns both the fixed Dafny and the translated C#.
///
/// This is what a human does: read the test failure, look at the code,
/// understand what went wrong, fix the logic, recompile, re-verify, re-run.
/// </summary>
public sealed class DafnyFixer
{
    private readonly IModelGateway _model;
    private readonly Z3Runner _z3;

    public DafnyFixer(IModelGateway model, Z3Runner z3)
    {
        _model = model;
        _z3 = z3;
    }

    /// <summary>
    /// Fix Dafny logic that produces wrong output. Returns the fixed Dafny
    /// source and translated C# if Z3 verification passes, or null if the
    /// model couldn't fix it or Z3 rejected the fix.
    /// </summary>
    public async Task<DafnyFixResult?> FixAsync(
        string dafnySource,
        string moduleName,
        string responsibility,
        string[] testCaseDescriptions,
        string[] fixInstructions,
        PhaseContext context,
        CancellationToken ct = default)
    {
        var maxAttempts = 3;
        var currentSource = dafnySource;
        var currentErrors = fixInstructions;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var systemPrompt = BuildFixerPrompt(currentSource, moduleName, responsibility, testCaseDescriptions, currentErrors);

            var prompt = new PromptTemplate
            {
                PhaseId = context.PhaseId,
                Version = new PromptVersion("1.0.0"),
                SystemPrompt = systemPrompt,
                OutputFormatSpec = "Fixed Dafny source code only (complete module)",
                ModelTier = ModelTier.Fast,
                Temperature = 0.1,
                MaxOutputTokens = 8192,
                OutputFormat = OutputFormat.PlainText,
                OutputSchemaRef = "DafnyModule",
                Status = PromptStatus.Active
            };

            string? fixedDafny;
            try
            {
                var gen = await _model.GenerateAsync(context.ModelRoute, prompt, context, ct);
                if (string.IsNullOrWhiteSpace(gen.Text))
                {
                    Console.Error.WriteLine($"[dafny-fixer] Attempt {attempt + 1}: model returned empty");
                    return null;
                }

                var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim();

                // Strip markdown fences if present
                var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                    text, @"```(?:dafny)?\s*\n?(.*?)\n?```",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (fenceMatch.Success)
                {
                    fixedDafny = fenceMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Model may wrap Dafny in JSON — extract the code field
                    fixedDafny = ExtractDafnyFromText(text);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[dafny-fixer] Attempt {attempt + 1}: model call failed: {ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(fixedDafny))
            {
                Console.Error.WriteLine($"[dafny-fixer] Attempt {attempt + 1}: extracted empty Dafny");
                return null;
            }

            // Z3 must verify the fix — Z3 is the judge, not the model.
            // If Z3 rejects, feed the error back and retry (let the dog chew on it).
            var stagingDir = Path.Combine(Path.GetTempPath(), "posit-dafny-fixer", context.SessionId.Value);
            Directory.CreateDirectory(stagingDir);
            var dafnyPath = Path.Combine(stagingDir, $"{moduleName}.dfy");
            await File.WriteAllTextAsync(dafnyPath, fixedDafny, ct);

            var verifyResult = await _z3.VerifyAsync(dafnyPath, ct);
            if (verifyResult.Success)
            {
                // Translate verified Dafny to C#
                var translation = await _z3.TranslateAsync(dafnyPath, moduleName, ct);
                if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
                {
                    Console.Error.WriteLine($"[dafny-fixer] Attempt {attempt + 1}: Z3 passed but C# translation failed: {translation.Stderr}");
                    // Translation failed — feed the translation error back and retry
                    currentSource = fixedDafny;
                    currentErrors = new[] { $"C# translation failed: {translation.Stderr}", "Fix the Dafny so it translates to C# correctly." };
                    continue;
                }

                Console.Error.WriteLine($"[dafny-fixer] Z3 verified + translated '{moduleName}' on attempt {attempt + 1} — fix accepted");
                return new DafnyFixResult(fixedDafny, translation.CleanCsharp, dafnyPath);
            }

            // Z3 rejected — feed the errors back for the next attempt
            Console.Error.WriteLine($"[dafny-fixer] Attempt {attempt + 1}/{maxAttempts}: Z3 rejected for '{moduleName}':");
            foreach (var err in verifyResult.Errors.Take(10))
                Console.Error.WriteLine($"  {err}");

            currentSource = fixedDafny;
            currentErrors = verifyResult.Errors.Length > 0
                ? verifyResult.Errors.Take(10).ToArray()
                : new[] { "Z3 verification failed (no specific error details)" };
        }

        Console.Error.WriteLine($"[dafny-fixer] Exhausted {maxAttempts} attempts for '{moduleName}'");
        return null;
    }

    private static string BuildFixerPrompt(
        string dafnySource,
        string moduleName,
        string responsibility,
        string[] testCaseDescriptions,
        string[] fixInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Dafny logic fixer. The module below compiles and passes Z3 verification,");
        sb.AppendLine("but produces WRONG OUTPUT when tested. The logic is incorrect (\"cotton candy\").");
        sb.AppendLine("Fix ONLY the logic that causes the test failures. Keep method signatures,");
        sb.AppendLine("{:extern} declarations, and requires/ensures contracts unchanged.");
        sb.AppendLine("Output the complete fixed Dafny module.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("1. Do NOT add new methods, new {:extern} declarations, or new modules.");
        sb.AppendLine("2. Do NOT add a Main method — the entry point is handled by C# wiring, not Dafny.");
        sb.AppendLine("3. Fix ONLY the body of existing methods that have wrong logic.");
        sb.AppendLine("4. Keep the module structure exactly as-is — same methods, same signatures, same externs.");
        sb.AppendLine();

        sb.AppendLine($"═══ MODULE: {moduleName} ═══");
        sb.AppendLine($"Responsibility: {responsibility}");
        sb.AppendLine();

        sb.AppendLine("═══ TEST CASE DESCRIPTIONS (what the module SHOULD do) ═══");
        foreach (var desc in testCaseDescriptions)
            sb.AppendLine($"  {desc}");
        sb.AppendLine();

        sb.AppendLine("═══ PROBLEMS TO FIX ═══");
        foreach (var instr in fixInstructions)
            sb.AppendLine($"  {instr}");
        sb.AppendLine();

        sb.AppendLine("═══ DAFNY REFERENCE CARD ═══");
        sb.AppendLine(LoadReferenceCard());
        sb.AppendLine();

        sb.AppendLine("═══ DAFNY SOURCE TO FIX ═══");
        sb.AppendLine(dafnySource);
        sb.AppendLine();
        sb.AppendLine("═══ END ═══");
        sb.AppendLine("Output the complete fixed Dafny module. ONLY Dafny code, no explanations, no markdown fences.");
        sb.AppendLine("The code MUST pass Z3 verification. Keep all {:extern} declarations unchanged.");

        return sb.ToString();
    }

    /// <summary>
    /// Extract Dafny code from model output. The model may return:
    /// - Raw Dafny (best case)
    /// - JSON wrapper with a code field (e.g. {"fixed_code": "...", "code": "..."})
    /// - Markdown fences (handled by caller)
    /// This mirrors what a human does: read the output, find the code, ignore the noise.
    /// </summary>
    private static string ExtractDafnyFromText(string text)
    {
        // If it doesn't start with {, it's probably raw Dafny
        var jsonStart = text.IndexOf('{');
        if (jsonStart < 0)
            return text.Trim();

        // Try to parse as JSON and extract the code — universally scan for
        // any string property that looks like Dafny (contains 'method' or 'module').
        // Don't enumerate field names — the model uses unpredictable names.
        try
        {
            var jsonText = text[jsonStart..];
            using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    if (s != null && (s.Contains("method ") || s.Contains("module ")))
                        return s.Trim();
                }
            }
        }
        catch { }

        // Not JSON or no code field found — return as-is
        return text.Trim();
    }

    private static string LoadReferenceCard()
    {
        var paths = new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "patterns", "dafny-reference-card.dfy"),
            "C:/Users/goldf/Posit/patterns/dafny-reference-card.dfy"
        };
        foreach (var p in paths)
            if (File.Exists(p))
                return File.ReadAllText(p);
        return "// Dafny reference card not found";
    }
}

/// <summary>
/// Result of a Dafny fix: the fixed Dafny source, the translated C#, and
/// the path to the .dfy file on disk.
/// </summary>
public sealed record DafnyFixResult(string FixedDafny, string TranslatedCSharp, string DafnyPath);