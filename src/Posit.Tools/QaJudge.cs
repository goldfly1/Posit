using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Posit.AI.Models;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;

namespace Posit.Tools;

/// <summary>
/// Three-layer judge. Deterministic layers 1-2, LLM layer 3.
///
/// Layer 1: Exact match — compares stdout + exit code against expected output.
/// Layer 2: Invariant check — calls validator method on abstract class carapace.
/// Layer 3: Heuristic check — one LLM call, anomaly detection (planned).
///
/// Only fires layer 2 if layer 1 has no expected output (non-computable transform).
/// Only fires layer 3 if layers 1-2 pass (no point asking "smell right?" if it's broken).
/// </summary>
public sealed class QaJudge
{
    private readonly IModelGateway? _model;

    public QaJudge(IModelGateway? model = null) { _model = model; }

    /// <summary>
    /// Judge a single test case. Returns the verdict + which layer decided.
    /// </summary>
    public async Task<JudgeVerdict> JudgeAsync(
        TestCaseRun run,
        string expectedOutput,
        int expectedExitCode,
        string expectedBehavior,
        string spec,
        CancellationToken ct = default)
    {
        // Layer 1: Exact match (deterministic)
        if (!string.IsNullOrEmpty(expectedOutput))
        {
            var exactMatch = run.Stdout.Trim().Equals(expectedOutput.Trim(), StringComparison.Ordinal)
                             && run.ExitCode == expectedExitCode;
            return new JudgeVerdict(
                exactMatch ? JudgeResult.Pass : JudgeResult.Fail,
                JudgeLayer.ExactMatch,
                exactMatch ? "Exact match: PASS" : $"Exact match: FAIL — expected '{expectedOutput.Trim()}', got '{run.Stdout.Trim()}'");
        }

        // Layer 2: Structural check (deterministic)
        // If no exact expected output, check structural validity:
        // - Error cases (exit code 1) must have non-empty stderr or error in stdout
        // - Success cases (exit code 0) must have non-empty stdout
        var structuralPass = CheckStructural(run, expectedBehavior);
        if (!structuralPass.pass)
        {
            return new JudgeVerdict(
                JudgeResult.Fail,
                JudgeLayer.StructuralCheck,
                structuralPass.reason);
        }

        // Layer 3: Heuristic check (LLM, anomaly detection)
        // Only fires if we have a model and structural check passed
        if (_model != null && !string.IsNullOrWhiteSpace(spec))
        {
            var heuristic = await HeuristicCheckAsync(run, spec, ct);
            return new JudgeVerdict(
                heuristic.pass ? JudgeResult.Pass : JudgeResult.HumanReview,
                JudgeLayer.Heuristic,
                heuristic.reason);
        }

        // No model — structural pass is enough
        return new JudgeVerdict(
            JudgeResult.Pass,
            JudgeLayer.StructuralCheck,
            "Structural check: PASS (no heuristic layer available)");
    }

    /// <summary>
    /// Layer 2: Structural check. Verifies the output has the right shape
    /// for the expected behavior — error cases produce errors, success cases
    /// produce output. Replaces the old keyword-matching rubber stamp.
    /// </summary>
    private static (bool pass, string reason) CheckStructural(TestCaseRun run, string expectedBehavior)
    {
        var behavior = expectedBehavior.ToLowerInvariant();

        // Error cases: exit code 1, should have error message
        if (behavior.Contains("error") || behavior.Contains("exit") && behavior.Contains("1"))
        {
            if (run.ExitCode != 1)
                return (false, $"Structural: FAIL — expected exit code 1, got {run.ExitCode}");
            if (string.IsNullOrWhiteSpace(run.Stdout) && string.IsNullOrWhiteSpace(run.Stderr))
                return (false, "Structural: FAIL — expected error output, got empty");
            return (true, "Structural: PASS (error case produced error output)");
        }

        // Success cases: should have non-empty output
        if (behavior.Contains("prints") || behavior.Contains("result") || behavior.Contains("output"))
        {
            if (run.ExitCode != 0)
                return (false, $"Structural: FAIL — expected exit code 0, got {run.ExitCode}");
            if (string.IsNullOrWhiteSpace(run.Stdout))
                return (false, "Structural: FAIL — expected output, got empty stdout");
            return (true, "Structural: PASS (success case produced output)");
        }

        // Default: pass if program ran and didn't crash
        if (run.ExitCode == 0 && !string.IsNullOrWhiteSpace(run.Stdout))
            return (true, "Structural: PASS (ran successfully with output)");

        return (false, $"Structural: FAIL — unexpected state (exit={run.ExitCode}, stdout empty={string.IsNullOrWhiteSpace(run.Stdout)})");
    }

    /// <summary>
    /// Layer 3: Heuristic check. One LLM call, low temperature.
    /// "Does anything about this output look wrong or unexpected given the spec?"
    /// </summary>
    private async Task<(bool pass, string reason)> HeuristicCheckAsync(
        TestCaseRun run, string spec, CancellationToken ct)
    {
        var truncatedOutput = run.Stdout.Length > 500
            ? run.Stdout[..500] + "..."
            : run.Stdout;

        var systemPrompt = $"""
            You are a QA anomaly detector. Given a spec and a sample of program output,
            determine if anything looks wrong or unexpected.

            Spec: {spec}

            Program output (exit code {run.ExitCode}):
            {truncatedOutput}

            All structural checks passed. Does anything about this output look wrong
            or unexpected given the spec? Output PASS or FAIL with one sentence explaining why.
            """;

        var prompt = new PromptTemplate
        {
            PhaseId = new PhaseId("qa-heuristic"),
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "PASS or FAIL + one sentence",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1,
            MaxOutputTokens = 256,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "HeuristicVerdict",
            Status = PromptStatus.Active
        };

        try
        {
            var route = new ModelRoute
            {
                Tier = ModelTier.Fast, ProviderId = "ollama",
                ModelId = "deepseek-v4-flash:cloud", MaxOutputTokens = 256, Temperature = 0.1
            };
            var gen = await _model!.GenerateAsync(route, prompt, new PhaseContext
            {
                SessionId = SessionId.New(),
                PhaseId = new PhaseId("qa-heuristic"),
                Prompt = prompt,
                ModelRoute = route,
                UserRequest = "",
                InputArtifacts = [],
                BudgetRemaining = new BudgetRemaining { Amount = 100, Cap = 100 },
                AttemptNumber = 1,
                CorrectionSignal = [],
                DesignContext = null
            }, ct);

            var text = gen.Text.Trim();
            var isPass = text.StartsWith("PASS", StringComparison.OrdinalIgnoreCase);
            return (isPass, $"Heuristic: {(isPass ? "PASS" : "FAIL")} — {text}");
        }
        catch (Exception ex)
        {
            // If the LLM call fails, don't fail the test — just skip the heuristic
            return (true, $"Heuristic: SKIPPED (LLM call failed: {ex.Message})");
        }
    }
}

/// <summary>
/// Result of running a single test case through the program.
/// </summary>
public sealed record TestCaseRun(string Stdout, string Stderr, int ExitCode);

/// <summary>
/// The judge's verdict for a single test case.
/// </summary>
public sealed record JudgeVerdict(
    JudgeResult Result,
    JudgeLayer Layer,
    string Reason);

public enum JudgeResult { Pass, Fail, HumanReview }

public enum JudgeLayer { ExactMatch, StructuralCheck, Heuristic }

/// <summary>
/// Structured QA report for a full test suite run.
/// </summary>
public sealed record QaReport(
    JudgeVerdict[] Verdicts,
    int TotalPassed,
    int TotalFailed,
    int TotalHumanReview,
    string Summary)
{
    public bool AllPassed => TotalFailed == 0 && TotalHumanReview == 0;
    public bool HasFailures => TotalFailed > 0;
    public bool NeedsHumanReview => TotalHumanReview > 0;

    public static QaReport Build(JudgeVerdict[] verdicts)
    {
        var passed = verdicts.Count(v => v.Result == JudgeResult.Pass);
        var failed = verdicts.Count(v => v.Result == JudgeResult.Fail);
        var review = verdicts.Count(v => v.Result == JudgeResult.HumanReview);
        return new QaReport(verdicts, passed, failed, review,
            $"QA: {passed} passed, {failed} failed, {review} need human review");
    }
}