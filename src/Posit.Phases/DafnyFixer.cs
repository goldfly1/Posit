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
        var systemPrompt = BuildFixerPrompt(dafnySource, moduleName, responsibility, testCaseDescriptions, fixInstructions);

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "Fixed Dafny source code only (complete module)",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1, // low temperature — targeted logic fix, not creative work
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
                return null;

            var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim();

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:dafny)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
                fixedDafny = fenceMatch.Groups[1].Value.Trim();
            else
                fixedDafny = text.Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[dafny-fixer] Model call failed: {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(fixedDafny))
            return null;

        // Z3 must verify the fix — Z3 is the judge, not the model
        var stagingDir = Path.Combine(Path.GetTempPath(), "posit-dafny-fixer", context.SessionId.Value);
        Directory.CreateDirectory(stagingDir);
        var dafnyPath = Path.Combine(stagingDir, $"{moduleName}.dfy");
        await File.WriteAllTextAsync(dafnyPath, fixedDafny, ct);

        var verifyResult = await _z3.VerifyAsync(dafnyPath, ct);
        if (!verifyResult.Success)
        {
            Console.Error.WriteLine($"[dafny-fixer] Z3 rejected the fix for '{moduleName}':");
            foreach (var err in verifyResult.Errors.Take(10))
                Console.Error.WriteLine($"  {err}");
            return null;
        }

        // Translate verified Dafny to C#
        var translation = await _z3.TranslateAsync(dafnyPath, moduleName, ct);
        if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
        {
            Console.Error.WriteLine($"[dafny-fixer] C# translation failed for '{moduleName}': {translation.Stderr}");
            return null;
        }

        Console.Error.WriteLine($"[dafny-fixer] Z3 verified + translated '{moduleName}' — fix accepted");
        return new DafnyFixResult(fixedDafny, translation.CleanCsharp, dafnyPath);
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

        sb.AppendLine("═══ DAFNY SOURCE TO FIX ═══");
        sb.AppendLine(dafnySource);
        sb.AppendLine();
        sb.AppendLine("═══ END ═══");
        sb.AppendLine("Output the complete fixed Dafny module. ONLY Dafny code, no explanations, no markdown fences.");
        sb.AppendLine("The code MUST pass Z3 verification. Keep all {:extern} declarations unchanged.");

        return sb.ToString();
    }
}

/// <summary>
/// Result of a Dafny fix: the fixed Dafny source, the translated C#, and
/// the path to the .dfy file on disk.
/// </summary>
public sealed record DafnyFixResult(string FixedDafny, string TranslatedCSharp, string DafnyPath);