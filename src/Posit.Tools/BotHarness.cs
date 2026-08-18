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
/// generates .csproj/.sln, builds in Docker, runs CLI test cases, compares output.
/// </summary>
public sealed class BotHarness
{
    private readonly ArtifactRepository _repo;
    private readonly string _dockerPath;
    private readonly IModelGateway? _model;

    public BotHarness(ArtifactRepository repo, string? dockerPath = null, IModelGateway? model = null)
    {
        _repo = repo;
        _dockerPath = dockerPath ?? "docker";
        _model = model;
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

        var runtimeDir = Path.Combine(tempDir, "DafnyRuntime");
        Directory.CreateDirectory(runtimeDir);
        var runtimeDllSource = FindDafnyRuntimeDll();
        if (runtimeDllSource != null)
            File.Copy(runtimeDllSource, Path.Combine(runtimeDir, "DafnyRuntime.dll"), true);

        File.WriteAllText(Path.Combine(tempDir, "Dockerfile.run"),
            BotHarnessDocker.GenerateDockerfileRun(cliComponent.Name));

        // Create test data files BEFORE Docker build so they're in the build context.
        // Use AI-generated test data from QA artifact if available, else stopgap.
        var testCases = ExtractTestCases(cliComponent, testSuite);
        var aiTestData = testSuite?.TestFiles ?? [];
        // Build a spec hint from component pattern names for test data generation
        var specHint = string.Join(" ", contract.Components
            .Where(c => c.PatternName != null)
            .Select(c => c.PatternName!));
        foreach (var tc in testCases)
        {
            // Try AI-generated test data first (from QA phase)
            var aiFile = aiTestData.FirstOrDefault(f =>
                f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase));
            var testData = aiFile?.Content ?? GenerateTestData(tc.Id, tc.Name, specHint);

            // Skip file creation for file-not-found tests — pass a bad path instead
            if (testData == "__NONEXISTENT_FILE__") continue;
            // Use .json extension for JSON test data, .csv for CSV, .txt for text
            var ext = testData.StartsWith("[") || testData.StartsWith("{") ? ".json"
                : (testData.Contains(",") && testData.Contains("\n") ? ".csv" : ".txt");
            var testFile = Path.Combine(tempDir, $"testdata_{tc.Id}{ext}");
            File.WriteAllText(testFile, testData);
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

            if (tc.Name.Contains("FileNotFound", StringComparison.OrdinalIgnoreCase)
                || tc.Name.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || tc.Name.Contains("missing", StringComparison.OrdinalIgnoreCase))
            {
                cliArg = "nonexistent_file.csv";
            }
            else if (isStdinEntry)
            {
                // Stdin-type program: pipe test data via stdin, no CLI arg
                cliArg = "";
                var aiFile = aiTestData.FirstOrDefault(f =>
                    f.Path.Contains(tc.Id, StringComparison.OrdinalIgnoreCase));
                stdinInput = aiFile?.Content ?? GenerateTestData(tc.Id, tc.Name, specHint);
                if (stdinInput == "__NONEXISTENT_FILE__") stdinInput = "";
            }
            else
            {
                // File-type program: pass test data file path as CLI arg
                var testData = GenerateTestData(tc.Id, tc.Name, specHint);
                var ext = testData.StartsWith("[") || testData.StartsWith("{") ? ".json"
                    : (testData.Contains(",") && testData.Contains("\n") ? ".csv" : ".txt");
                cliArg = $"testdata_{tc.Id}{ext}";
            }
            var runResult = await BotHarnessDocker.RunContainerAsync(
                _dockerPath, sessionId.Value, cliComponent.Name, cliArg, ct, stdinInput);
            results.Add(new TestCaseResult(tc.Id, tc.Name, runResult.Success, runResult.Output,
                tc.ExpectedBehavior, CompareOutput(runResult.Output, tc.ExpectedBehavior)));
        }

        // LLM failure analysis: if any tests failed, ask the model to classify
        var failures = results.Where(r => !r.Matches).ToList();
        if (failures.Count > 0 && _model != null)
        {
            var analysis = await AnalyzeFailuresAsync(failures, cliComponent, ct);
            if (!string.IsNullOrEmpty(analysis))
                Console.Error.WriteLine($"[harness] LLM failure analysis:\n{analysis}");
        }

        return new BotHarnessResult(results.All(r => r.Matches), [.. results], tempDir, null);
    }

    internal static bool TryGetArtifact(ArtifactBundle[] artifacts, ArtifactKind kind, out string? error)
    {
        error = artifacts.Any(a => a.Kind == kind) ? null : $"No {kind} artifact found for session";
        return error == null;
    }

    internal static Component? FindCliComponent(ArchitectureContract contract)
    {
        var withConnections = contract.Components.Where(c => c.Connections.Length > 0).ToArray();
        if (withConnections.Length > 0) return withConnections[0];
        return contract.Components.FirstOrDefault(c => c.Classification == ModuleClassification.IoShell)
            ?? contract.Components.FirstOrDefault();
    }

    internal static SourceCodeFile[] DeduplicateFiles(SourceCodeFile[] files)
    {
        var dict = new Dictionary<string, SourceCodeFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) dict[f.Path] = f; // keep last occurrence (dedup by path)
        return [.. dict.Values];
    }

    internal static string? FindDafnyRuntimeDll()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "DafnyRuntime.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "DafnyRuntime.dll"),
            "C:/Users/goldf/Posit/src/Posit.DafnyRuntime/DafnyRuntime.dll",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    internal static TestCaseInfo[] ExtractTestCases(Component component, TestSuite? testSuite)
    {
        if (component.TestCases.Length > 0)
            return [.. component.TestCases.Select(tc => new TestCaseInfo(tc.Id, tc.Name, tc.Description, tc.ExpectedBehavior))];
        return [new TestCaseInfo("smoke", "Smoke test", "", "exit code 0")];
    }

    internal static bool CompareOutput(string actual, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        // Exact match
        if (actual.Trim() == expected.Trim()) return true;
        // Substring match (for prose expected behavior)
        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase)) return true;
        // Exit code match
        if (actual.Contains("exit code 0", StringComparison.OrdinalIgnoreCase)
            && expected.Contains("exit code 0", StringComparison.OrdinalIgnoreCase)) return true;
        // JSON array check: if expected mentions "JSON array" and output starts with [
        if (expected.Contains("JSON array", StringComparison.OrdinalIgnoreCase)
            && actual.TrimStart().StartsWith('[') && actual.TrimEnd().EndsWith(']'))
            return true;
        // JSON object check: if expected mentions "JSON object" and output starts with {
        if (expected.Contains("JSON object", StringComparison.OrdinalIgnoreCase)
            && actual.TrimStart().StartsWith('{') && actual.TrimEnd().EndsWith('}'))
            return true;
        // Error check: if expected mentions "error" or "non-zero" and output contains error/failed/exception
        if ((expected.Contains("error", StringComparison.OrdinalIgnoreCase)
             || expected.Contains("non-zero", StringComparison.OrdinalIgnoreCase))
            && (actual.Contains("error", StringComparison.OrdinalIgnoreCase)
                || actual.Contains("exception", StringComparison.OrdinalIgnoreCase)
                || actual.Contains("failed", StringComparison.OrdinalIgnoreCase)))
            return true;
        // Successful run with output = pass (if expected mentions "output" or "completes")
        if ((expected.Contains("valid", StringComparison.OrdinalIgnoreCase)
             || expected.Contains("output", StringComparison.OrdinalIgnoreCase)
             || expected.Contains("completes", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(actual))
            return true;
        // CSV check: if expected mentions CSV and output has commas + newlines
        if (expected.Contains("CSV", StringComparison.OrdinalIgnoreCase)
            && actual.Contains(",") && actual.Contains("\n"))
            return true;
        // Empty output check: if expected mentions "empty" or "no output"
        if ((expected.Contains("empty", StringComparison.OrdinalIgnoreCase)
             || expected.Contains("no output", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(actual))
            return true;
        return false;
    }

    /// <summary>
    /// Generate basic test data for a test case. This is a stopgap until the
    /// pseudo-data generation system (#5) is built. Creates CSV content based
    /// on the test case name — valid CSV, empty file, or invalid CSV.
    /// </summary>
    internal static string GenerateTestData(string tcId, string tcName, string? specHint = null)
    {
        // Match test case names OR spec hints to generate appropriate data
        var name = (tcName + " " + (specHint ?? "")).ToLowerInvariant();
        if (name.Contains("empty") || name.Contains("no data") || name.Contains("emptyarray"))
        {
            if (name.Contains("json") || name.Contains("array"))
                return "[]";
            return "";
        }
        if (name.Contains("invalid") || name.Contains("inconsistent") || name.Contains("mismatch"))
            return "name,age,city\nAlice,30,NYC\nBob,25,LA,extra\nCarol,35,SF";
        if (name.Contains("filenotfound") || name.Contains("missing") || name.Contains("not found"))
            return "__NONEXISTENT_FILE__";
        if (name.Contains("json"))
            return "[{\"name\":\"Alice\",\"age\":\"30\"},{\"name\":\"Bob\",\"age\":\"25\"}]";
        if (name.Contains("word") || name.Contains("text") || name.Contains("frequency") || name.Contains("log"))
            return "the cat sat on the mat the cat\nthe dog ran fast\n";
        // Temperature/stdin programs: input is "value unit" format
        if (name.Contains("temp") || name.Contains("convert") || name.Contains("celsius") || name.Contains("fahrenheit"))
        {
            if (name.Contains("invalid") || name.Contains("error") || name.Contains("bad"))
                return "20 X";
            return "32 F";
        }
        if (name.Contains("valid") || name.Contains("well-formed") || name.Contains("produces"))
            return "name,age,city\nAlice,30,NYC\nBob,25,LA\nCarol,35,SF";
        // Default: simple valid CSV
        return "name,age\nAlice,30\nBob,25";
    }

    internal static T? Deserialize<T>(byte[] payloadJson) where T : class
    {
        var json = Encoding.UTF8.GetString(payloadJson);
        return JsonSerializer.Deserialize<T>(json, PositJson.Options);
    }

    private static BotHarnessResult Fail(string error) => new(false, [], null, error);

    /// <summary>
    /// LLM failure analysis. Asks the model to classify why tests failed
    /// and suggest fixes. Output is for diagnostics, not automated action.
    /// </summary>
    private async Task<string> AnalyzeFailuresAsync(List<TestCaseResult> failures, Component cliComp, CancellationToken ct)
    {
        var failureDesc = string.Join("\n", failures.Select(f =>
            $"  {f.Id}: expected={f.Expected}, actual={f.Output[..Math.Min(200, f.Output.Length)]}"));

        var systemPrompt = $"""
            You are the QA failure analyzer for the Posit spec compiler.
            The following test cases failed. Classify each failure and suggest a fix.

            Component: {cliComp.Name}
            Failures:
            {failureDesc}

            For each failure, output one line:
            - FAILURE_TYPE: brief description (e.g. "wrong output format", "crash on empty input", "type mismatch")
            - SUGGESTED_FIX: what to change (e.g. "add null check", "convert type at boundary")
            """;

        var prompt = new PromptTemplate
        {
            PhaseId = new PhaseId("qa"),
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "One line per failure",
            ModelTier = ModelTier.Fast,
            Temperature = 0.2,
            MaxOutputTokens = 2048,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "FailureAnalysis",
            Status = PromptStatus.Active
        };

        try
        {
            var route = new ModelRoute
            {
                Tier = ModelTier.Fast, ProviderId = "ollama",
                ModelId = "deepseek-v4-flash:cloud", MaxOutputTokens = 2048, Temperature = 0.2
            };
            var gen = await _model!.GenerateAsync(route, prompt, new PhaseContext
            {
                SessionId = SessionId.New(),
                PhaseId = new PhaseId("qa"),
                Prompt = prompt,
                UserRequest = "",
                InputArtifacts = [],
                ModelRoute = route,
                BudgetRemaining = new BudgetRemaining { Amount = 1000, Cap = 1000 },
                AttemptNumber = 1,
                CorrectionSignal = null,
                DesignContext = null
            }, ct);
            return gen.Text;
        }
        catch (Exception ex)
        {
            return $"[analysis failed: {ex.Message}]";
        }
    }
}

public sealed record BotHarnessResult(bool Success, TestCaseResult[] Results, string? TempDir, string? Error);
public sealed record TestCaseResult(string Id, string Name, bool Ran, string Output, string Expected, bool Matches);
internal sealed record TestCaseInfo(string Id, string Name, string Input, string ExpectedBehavior);