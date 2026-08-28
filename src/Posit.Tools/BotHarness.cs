using System.Text;
using System.Text.Json;
using Posit.AI.Models;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Data.Repositories;

namespace Posit.Tools;

/// <summary>
/// The bot harness: loads artifacts from DB, materializes source files,
/// generates .csproj/.sln, builds in Docker, runs CLI test cases through the
/// three-layer judge (exact match → structural check → heuristic).
/// </summary>
public sealed class BotHarness
{
    private readonly ArtifactRepository _repo;
    private readonly string _dockerPath;
    private readonly QaJudge _judge;

    public BotHarness(ArtifactRepository repo, string? dockerPath = null, QaJudge? judge = null)
    {
        _repo = repo;
        _dockerPath = dockerPath ?? "docker";
        _judge = judge ?? new QaJudge();
    }

    public async Task<BotHarnessResult> RunAsync(SessionId sessionId, CancellationToken ct = default)
    {
        var artifacts = await _repo.ListBySessionAsync(sessionId, ct);

        var contract = TryGetArtifact(artifacts, ArtifactKind.ArchitectureContract, out var archErr)
            ? Deserialize<ArchitectureContract>(artifacts.First(a => a.Kind == ArtifactKind.ArchitectureContract).PayloadJson)
            : null;
        if (contract == null) return Fail(archErr ?? "Could not deserialize ArchitectureContract");

        var sourceCode = TryGetArtifact(artifacts, ArtifactKind.SourceCodeBundle, out var srcErr)
            ? Deserialize<SourceCodeBundle>(artifacts.First(a => a.Kind == ArtifactKind.SourceCodeBundle).PayloadJson)
            : null;
        if (sourceCode == null) return Fail(srcErr ?? "Could not deserialize SourceCodeBundle");

        TestSuite? testSuite = null;
        var testBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.TestSuite);
        if (testBundle != null) testSuite = Deserialize<TestSuite>(testBundle.PayloadJson);

        var cliComponent = FindCliComponent(contract);
        if (cliComponent == null) return Fail("No CLI component found (need a component with connections)");

        var tempDir = Path.Combine(Path.GetTempPath(), "posit-bot-harness", sessionId.Value);
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        foreach (var file in DeduplicateFiles(sourceCode.Files))
        {
            var fullPath = Path.Combine(tempDir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        var projectNames = new List<string>();
        foreach (var comp in contract.Components)
        {
            var isExe = comp.Id == cliComponent.Id;
            var projName = comp.Name;
            var projDir = Path.Combine(tempDir, projName);
            Directory.CreateDirectory(projDir);
            // Collect dependencies from connections
            var deps = comp.Connections
                .Where(c => c.ToComponent != comp.Name)
                .Select(c => c.ToComponent)
                .Distinct()
                .ToList();
            File.WriteAllText(Path.Combine(projDir, $"{projName}.csproj"),
                BotHarnessProjects.GenerateCsproj(projName, isExe, deps));
            projectNames.Add(projName);
        }

        File.WriteAllText(Path.Combine(tempDir, "PositGenerated.sln"),
            BotHarnessProjects.GenerateSln("PositGenerated", projectNames));

        File.WriteAllText(Path.Combine(tempDir, "Dockerfile.run"),
            BotHarnessDocker.GenerateDockerfileRun(cliComponent.Name));

        // Create test data files BEFORE Docker build so they're in the build context.
        // Test data comes from the QA artifact (architect's test cases).
        var testCases = ExtractTestCases(cliComponent, testSuite);
        var aiTestData = testSuite?.TestFiles ?? [];
        foreach (var tc in testCases)
        {
            // Match by index: test files are stdin_0.txt, stdin_1.txt, etc.
            // Also try matching by tc.Id in the path (legacy format)
            var aiFile = aiTestData.Length > 0
                ? aiTestData.Length > testCases.IndexOf(tc)
                    ? aiTestData[testCases.IndexOf(tc)]
                    : aiTestData.FirstOrDefault(f => f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase))
                : aiTestData.FirstOrDefault(f => f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase));
            var testData = aiFile?.Content ?? tc.Input;

            // Skip file creation for file-not-found tests — pass a bad path instead
            if (testData == "__NONEXISTENT_FILE__") continue;

            // Multi-file: testData contains === separator → create multiple files
            if (testData.Contains("==="))
            {
                var parts = testData.Split("===", 2);
                for (var pi = 0; pi < parts.Length; pi++)
                {
                    var partData = parts[pi].Trim();
                    var partExt = partData.StartsWith("[") || partData.StartsWith("{") ? ".json"
                        : (partData.Contains(",") && partData.Contains("\n") ? ".csv" : ".txt");
                    var testFile = Path.Combine(tempDir, $"testdata_{tc.Id}_{pi}{partExt}");
                    File.WriteAllText(testFile, partData);
                }
            }
            else
            {
                // Single file: .json extension for JSON, .csv for CSV, .txt for text
                var ext = testData.StartsWith("[") || testData.StartsWith("{") ? ".json"
                    : (testData.Contains(",") && testData.Contains("\n") ? ".csv" : ".txt");
                var testFile = Path.Combine(tempDir, $"testdata_{tc.Id}{ext}");
                File.WriteAllText(testFile, testData);
            }
        }

        var buildResult = await BotHarnessDocker.BuildAsync(_dockerPath, tempDir, sessionId.Value, ct);
        if (!buildResult.Success) return new BotHarnessResult(false, [], tempDir, $"Docker build failed: {buildResult.Output}");

        var results = new List<TestCaseResult>();

        // Run each test case — test data files already created above
        // Check if the CLI component uses stdin entry (not file args)
        var isStdinEntry = string.Equals(cliComponent.EntryType, "stdin", StringComparison.OrdinalIgnoreCase);

        foreach (var tc in testCases)
        {
            string cliArg;
            string? stdinInput = null;
            string fedInput = "";

            if (tc.Name.Contains("FileNotFound", StringComparison.OrdinalIgnoreCase)
                || tc.Name.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || tc.Name.Contains("missing", StringComparison.OrdinalIgnoreCase))
            {
                cliArg = "nonexistent_file.csv";
                fedInput = "(nonexistent file path — error-path test)";
            }
            else if (isStdinEntry)
            {
                // Stdin-type program: pipe test data via stdin, no CLI arg
                cliArg = "";
                // Match by index (same as file creation above)
                var stdinAiFile = aiTestData.Length > 0
                    ? aiTestData.Length > testCases.IndexOf(tc)
                        ? aiTestData[testCases.IndexOf(tc)]
                        : aiTestData.FirstOrDefault(f => f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase))
                    : aiTestData.FirstOrDefault(f => f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase));
                stdinInput = stdinAiFile?.Content ?? tc.Input;
                if (stdinInput == "__NONEXISTENT_FILE__") stdinInput = "";
                fedInput = stdinInput ?? "";
            }
            else
            {
                // File-type program: pass test data file path as CLI arg.
                // Use the SAME test data that was used to create the file (above),
                // so the extension matches the file on disk.
                var testData = aiTestData.Length > 0
                    ? aiTestData.Length > testCases.IndexOf(tc)
                        ? aiTestData[testCases.IndexOf(tc)].Content
                        : aiTestData.FirstOrDefault(f => f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase))?.Content
                    : null;
                testData ??= tc.Input;
                fedInput = testData;

                // Multi-file: pass multiple file paths
                if (testData.Contains("==="))
                {
                    var parts = testData.Split("===", 2);
                    var args = new List<string>();
                    for (var pi = 0; pi < parts.Length; pi++)
                    {
                        var partData = parts[pi].Trim();
                        var ext = partData.StartsWith("[") || partData.StartsWith("{") ? ".json"
                            : (partData.Contains(",") && partData.Contains("\n") ? ".csv" : ".txt");
                        args.Add($"testdata_{tc.Id}_{pi}{ext}");
                    }
                    cliArg = string.Join(" ", args);
                }
                else
                {
                    var ext = testData.StartsWith("[") || testData.StartsWith("{") ? ".json"
                        : (testData.Contains(",") && testData.Contains("\n") ? ".csv" : ".txt");
                    cliArg = $"testdata_{tc.Id}{ext}";
                }
            }
            var runResult = await BotHarnessDocker.RunContainerAsync(
                _dockerPath, sessionId.Value, cliComponent.Name, cliArg, ct, stdinInput);

            // Three-layer judge: exact match → structural check → heuristic
            var run = new TestCaseRun(runResult.Output, "", runResult.ExitCode);
            var verdict = await _judge.JudgeAsync(
                run, tc.ExpectedOutput, tc.ExpectedExitCode, tc.ExpectedBehavior,
                contract.SystemContext, ct);

            results.Add(new TestCaseResult(tc.Id, tc.Name, runResult.Success, runResult.Output,
                tc.ExpectedOutput.Length > 0 ? tc.ExpectedOutput : tc.ExpectedBehavior,
                verdict.Result == JudgeResult.Pass, verdict) { FedInput = fedInput });
        }

        // Report carries the REAL per-test verdicts (layer + reason) — no rebuild.
        var report = QaReport.Build([.. results
            .Where(r => r.Verdict != null)
            .Select(r => r.Verdict!)]);
        // If no verdicts were recorded (e.g. all cases failed before judging),
        // fall back to a failed report so success is never assumed.
        if (report.Verdicts.Length == 0 && results.Count > 0)
            report = QaReport.Build([.. results.Select(r => new JudgeVerdict(
                JudgeResult.Fail, JudgeLayer.ExactMatch, "No verdict recorded"))]);

        return new BotHarnessResult(results.All(r => r.Matches), [.. results], tempDir, null, report);
    }

    internal static bool TryGetArtifact(ArtifactBundle[] artifacts, ArtifactKind kind, out string? error)
    {
        error = artifacts.Any(a => a.Kind == kind) ? null : $"No {kind} artifact found for session";
        return error == null;
    }

    public static Component? FindCliComponent(ArchitectureContract contract)
    {
        var withConnections = contract.Components.Where(c => c.Connections.Length > 0).ToArray();
        if (withConnections.Length > 0) return withConnections[0];
        return contract.Components.FirstOrDefault(c => c.Classification == ModuleClassification.IoShell)
            ?? contract.Components.FirstOrDefault();
    }

    public static SourceCodeFile[] DeduplicateFiles(SourceCodeFile[] files)
    {
        var dict = new Dictionary<string, SourceCodeFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) dict[f.Path] = f; // keep last occurrence (dedup by path)
        return [.. dict.Values];
    }

    internal static TestCaseInfo[] ExtractTestCases(Component component, TestSuite? testSuite)
    {
        if (component.TestCases.Length > 0)
        {
            var cases = component.TestCases.Select((tc, i) =>
            {
                var key = $"tc{i + 1}";
                var expectedOutput = testSuite?.ExpectedOutputs?.TryGetValue(key, out var eo) == true ? eo : "";
                var expectedExit = testSuite?.ExpectedExitCodes?.TryGetValue(key, out var ee) == true ? ee : 0;
                // Input = the architect's CONCRETE input (Phase A contract field),
                // not the prose Description. The old mapping put Description here,
                // which fed prose into the program when no pseudodata file existed.
                return new TestCaseInfo(tc.Id, tc.Name, tc.Input, tc.ExpectedBehavior, expectedOutput, expectedExit);
            }).ToArray();
            return cases;
        }
        return [new TestCaseInfo("smoke", "Smoke test", "", "exit code 0")];
    }

    internal static T? Deserialize<T>(byte[] payloadJson) where T : class
    {
        var json = Encoding.UTF8.GetString(payloadJson);
        return JsonSerializer.Deserialize<T>(json, PositJson.Options);
    }

    private static BotHarnessResult Fail(string error) => new(false, [], null, error);
}

public sealed record BotHarnessResult(bool Success, TestCaseResult[] Results, string? TempDir, string? Error, QaReport? Report = null);
public sealed record TestCaseResult(string Id, string Name, bool Ran, string Output, string Expected, bool Matches, JudgeVerdict? Verdict = null)
{
    /// <summary>
    /// The input ACTUALLY fed to the program this run (pseudodata file content,
    /// stdin payload, or architect input). The failure report must show this —
    /// not the contract's intent — so the model debugs against reality.
    /// </summary>
    public string FedInput { get; init; } = "";
}
internal sealed record TestCaseInfo(string Id, string Name, string Input, string ExpectedBehavior, string ExpectedOutput = "", int ExpectedExitCode = 0);