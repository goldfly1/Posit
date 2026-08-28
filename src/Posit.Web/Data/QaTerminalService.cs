using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Data.Repositories;
using Posit.Tools;
using System.Text;
using System.Text.Json;

namespace Posit.Web.Data;

/// <summary>
/// Loads architect contracts, source code, and test suites from Postgres.
/// Runs the program under test with given input and returns output + verdict.
/// This is the backend for the QA GUI — both the human and the deterministic bot
/// use the same API.
/// </summary>
public sealed class QaTerminalService
{
    private readonly ArtifactRepository _repo;

    public QaTerminalService(ArtifactRepository? repo = null)
    {
        _repo = repo ?? new ArtifactRepository();
    }

    /// <summary>
    /// Load a session's contract, source code, and test suite.
    /// Returns the form definition (fields from method signatures) + test data.
    /// </summary>
    public async Task<QaSession?> LoadSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var sid = new SessionId(sessionId);
        var artifacts = await _repo.ListBySessionAsync(sid, ct);

        var contractBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.ArchitectureContract);
        if (contractBundle == null) return null;
        var contract = Deserialize<ArchitectureContract>(contractBundle.PayloadJson);
        if (contract == null) return null;

        var sourceBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
        var sourceCode = sourceBundle != null ? Deserialize<SourceCodeBundle>(sourceBundle.PayloadJson) : null;

        var testBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.TestSuite);
        var testSuite = testBundle != null ? Deserialize<TestSuite>(testBundle.PayloadJson) : null;

        // Build form fields from the logic component's method signatures
        var cliComponent = BotHarness.FindCliComponent(contract);
        var logicComponent = contract.Components.FirstOrDefault(c => c.Classification == ModuleClassification.Logic)
                             ?? contract.Components.FirstOrDefault(c => c != cliComponent);

        var pages = BuildFormPages(contract, cliComponent, logicComponent);

        // Build pseudodata combos from the test suite
        var pseudodata = BuildPseudodata(testSuite, cliComponent);

        return new QaSession
        {
            SessionId = sessionId,
            SystemContext = contract.SystemContext,
            Pages = pages,
            PseudodataCombos = pseudodata,
            HasSourceCode = sourceCode != null,
            CliComponentName = cliComponent?.Name ?? "Program",
            IsStdinEntry = string.Equals(cliComponent?.EntryType, "stdin", StringComparison.OrdinalIgnoreCase),
            UniversalFields = BuildUniversalFields(),
            Actions = BuildActions(pages.Length),
            PageNames = pages.Select(p => p.Name).ToArray()
        };
    }

    /// <summary>
    /// Universal fields that appear on every trial — always present, blank when not used.
    /// The bot Tabs through all of them; fills the ones that matter, leaves the rest empty.
    /// </summary>
    private static UniversalField[] BuildUniversalFields()
    {
        return
        [
            new() { Name = "name", Label = "Name", Type = "string", TabOrder = 0 },
            new() { Name = "address", Label = "Address", Type = "string", TabOrder = 1 },
            new() { Name = "age", Label = "Age", Type = "int", TabOrder = 2 },
            new() { Name = "email", Label = "Email", Type = "string", TabOrder = 3 },
            new() { Name = "phone", Label = "Phone", Type = "string", TabOrder = 4 },
            new() { Name = "date", Label = "Date", Type = "date", TabOrder = 5 },
            new() { Name = "notes", Label = "Notes", Type = "string", TabOrder = 6 },
        ];
    }

    /// <summary>
    /// Action buttons that execute code. These are the operations a user or bot
    /// can trigger — each has a keyboard shortcut and calls an API endpoint.
    /// </summary>
    private static ActionButton[] BuildActions(int pageCount)
    {
        var actions = new List<ActionButton>
        {
            new() { Name = "search", Label = "🔍 Search", Shortcut = "Ctrl+S", ApiEndpoint = "/api/qa/search", HttpMethod = "POST", TabOrder = 100 },
            new() { Name = "save", Label = "💾 Save", Shortcut = "Ctrl+S+Shift", ApiEndpoint = "/api/qa/save", HttpMethod = "POST", TabOrder = 101 },
            new() { Name = "delete", Label = "🗑 Delete", Shortcut = "Ctrl+D", ApiEndpoint = "/api/qa/delete", HttpMethod = "POST", TabOrder = 102 },
            new() { Name = "run", Label = "▶ Run Test", Shortcut = "Enter", ApiEndpoint = "/api/qa/run", HttpMethod = "POST", TabOrder = 103 },
            new() { Name = "grind", Label = "🤖 Grind All", Shortcut = "Ctrl+G", ApiEndpoint = "/api/qa/grind", HttpMethod = "POST", TabOrder = 104 },
        };
        return actions.ToArray();
    }

    /// <summary>
    /// Run one test case: write input to temp file, run the program in Docker,
    /// return stdout/stderr/exit + judge verdict.
    /// </summary>
    public async Task<QaRunResult> RunTestCaseAsync(
        string sessionId, string input, string expectedOutput, int expectedExitCode,
        string expectedBehavior, string systemContext, bool isStdin,
        CancellationToken ct = default)
    {
        // Materialize source + build in Docker
        var sid = new SessionId(sessionId);
        var artifacts = await _repo.ListBySessionAsync(sid, ct);

        var contractBundle = artifacts.First(a => a.Kind == ArtifactKind.ArchitectureContract);
        var contract = Deserialize<ArchitectureContract>(contractBundle.PayloadJson)!;
        var sourceBundle = artifacts.First(a => a.Kind == ArtifactKind.SourceCodeBundle);
        var sourceCode = Deserialize<SourceCodeBundle>(sourceBundle.PayloadJson)!;

        var cliComponent = BotHarness.FindCliComponent(contract);
        var cliName = cliComponent?.Name ?? "Program";

        var tempDir = Path.Combine(Path.GetTempPath(), "posit-qa-gui", sessionId, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        foreach (var file in BotHarness.DeduplicateFiles(sourceCode.Files))
        {
            var fullPath = Path.Combine(tempDir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        var projectNames = new List<string>();
        foreach (var comp in contract.Components)
        {
            var isExe = comp.Id == cliComponent?.Id;
            var projName = comp.Name;
            var projDir = Path.Combine(tempDir, projName);
            Directory.CreateDirectory(projDir);
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
            BotHarnessDocker.GenerateDockerfileRun(cliName));

        // Write the test input
        string cliArg = "";
        string? stdinInput = null;

        if (isStdin)
        {
            stdinInput = input;
        }
        else
        {
            var ext = input.StartsWith("[") || input.StartsWith("{") ? ".json"
                : (input.Contains(",") && input.Contains("\n") ? ".csv" : ".txt");
            var testFile = Path.Combine(tempDir, $"testdata_gui{ext}");
            File.WriteAllText(testFile, input);
            cliArg = $"testdata_gui{ext}";
        }

        // Build
        var buildResult = await BotHarnessDocker.BuildAsync("docker", tempDir, sessionId, ct);
        if (!buildResult.Success)
        {
            return new QaRunResult(false, "", $"Docker build failed:\n{buildResult.Output}", -1,
                "Fail", "Build", "Build failed — source code does not compile");
        }

        // Run
        var runResult = await BotHarnessDocker.RunContainerAsync(
            "docker", sessionId, cliName, cliArg, ct, stdinInput);

        // Judge
        var judge = new QaJudge();
        var run = new TestCaseRun(runResult.Output, "", runResult.ExitCode);
        var verdict = await judge.JudgeAsync(
            run, expectedOutput, expectedExitCode, expectedBehavior, systemContext, ct);

        return new QaRunResult(
            runResult.Success,
            runResult.Output,
            "",
            runResult.ExitCode,
            verdict.Result.ToString(),
            verdict.Layer.ToString(),
            verdict.Reason);
    }

    /// <summary>
    /// Build form pages from the contract's method signatures.
    /// Each method = one page. Each parameter = one field.
    /// </summary>
    private static FormPage[] BuildFormPages(ArchitectureContract contract,
        Component? cliComponent, Component? logicComponent)
    {
        var pages = new List<FormPage>();

        if (logicComponent?.MethodSignatures.Length > 0)
        {
            foreach (var method in logicComponent.MethodSignatures)
            {
                pages.Add(new FormPage
                {
                    Name = method.Name,
                    Fields = method.Params.Select((p, i) => new FormField
                    {
                        Name = p.Name,
                        Label = p.Name,
                        Type = p.Type,
                        TabOrder = i,
                        // Mark the primary input field (string[] or double usually)
                        IsPrimary = i == 0
                    }).ToArray()
                });
            }
        }

        // Fallback: if no logic component methods, use CLI component
        if (pages.Count == 0 && cliComponent?.MethodSignatures.Length > 0)
        {
            foreach (var method in cliComponent.MethodSignatures)
            {
                pages.Add(new FormPage
                {
                    Name = method.Name,
                    Fields = method.Params.Select((p, i) => new FormField
                    {
                        Name = p.Name,
                        Label = p.Name,
                        Type = p.Type,
                        TabOrder = i,
                        IsPrimary = i == 0
                    }).ToArray()
                });
            }
        }

        // Ultimate fallback: single text area for stdin/file input
        if (pages.Count == 0)
        {
            pages.Add(new FormPage
            {
                Name = "Input",
                Fields = [new FormField { Name = "input", Label = "Input Data", Type = "string", TabOrder = 0, IsPrimary = true }]
            });
        }

        return pages.ToArray();
    }

    /// <summary>
    /// Build pseudodata combos from the test suite.
    /// Each combo = one set of field values the bot will type into the form.
    /// </summary>
    private static PseudodataCombo[] BuildPseudodata(TestSuite? testSuite, Component? cliComponent)
    {
        if (testSuite?.TestFiles.Length > 0)
        {
            return testSuite.TestFiles.Select((f, i) =>
            {
                var key = $"tc{i + 1}";
                var expectedOutput = testSuite.ExpectedOutputs?.TryGetValue(key, out var eo) == true ? eo : "";
                var expectedExit = testSuite.ExpectedExitCodes?.TryGetValue(key, out var ee) == true ? ee : 0;
                return new PseudodataCombo
                {
                    Index = i,
                    Label = $"Test {i + 1}: {Path.GetFileName(f.Path)}",
                    Input = f.Content,
                    ExpectedOutput = expectedOutput,
                    ExpectedExitCode = expectedExit,
                    Description = $"Test case {i + 1}"
                };
            }).ToArray();
        }

        // Fallback: use CLI component test cases
        if (cliComponent?.TestCases.Length > 0)
        {
            return cliComponent.TestCases.Select((tc, i) => new PseudodataCombo
            {
                Index = i,
                Label = $"Test {i + 1}: {tc.Name}",
                Input = tc.Description,
                ExpectedOutput = "",
                ExpectedExitCode = 0,
                Description = tc.ExpectedBehavior
            }).ToArray();
        }

        return [];
    }

    private static T? Deserialize<T>(byte[] payloadJson) where T : class
    {
        var json = Encoding.UTF8.GetString(payloadJson);
        return JsonSerializer.Deserialize<T>(json, PositJson.Options);
    }
}

public sealed class QaSession
{
    public string SessionId { get; set; } = "";
    public string SystemContext { get; set; } = "";
    public FormPage[] Pages { get; set; } = [];
    public PseudodataCombo[] PseudodataCombos { get; set; } = [];
    public bool HasSourceCode { get; set; }
    public string CliComponentName { get; set; } = "";
    public bool IsStdinEntry { get; set; }
    public UniversalField[] UniversalFields { get; set; } = [];
    public ActionButton[] Actions { get; set; } = [];
    public string[] PageNames { get; set; } = [];
}

/// <summary>
/// Universal fields that appear on every trial — name, address, age, etc.
/// These are always present; blank when not used. The bot Tabs through them
/// in order; the ones that matter get filled, the rest stay blank.
/// </summary>
public sealed class UniversalField
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";  // string, int, double, date, bool
    public int TabOrder { get; set; }
    public bool Required { get; set; }
}

/// <summary>
/// Action buttons that execute code: Search, Save, Delete, Run Test, etc.
/// Each has a keyboard shortcut and an API endpoint.
/// </summary>
public sealed class ActionButton
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Shortcut { get; set; } = "";  // e.g. "Enter", "Ctrl+Enter", "F2"
    public string ApiEndpoint { get; set; } = "";
    public string HttpMethod { get; set; } = "POST";
    public int TabOrder { get; set; }
}

public sealed class FormPage
{
    public string Name { get; set; } = "";
    public FormField[] Fields { get; set; } = [];
}

public sealed class FormField
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public int TabOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class PseudodataCombo
{
    public int Index { get; set; }
    public string Label { get; set; } = "";
    public string Input { get; set; } = "";
    public string ExpectedOutput { get; set; } = "";
    public int ExpectedExitCode { get; set; }
    public string Description { get; set; } = "";
}

public sealed class QaRunResult
{
    public bool Success { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public int ExitCode { get; set; }
    public string Verdict { get; set; } = "";
    public string JudgeLayer { get; set; } = "";
    public string Reason { get; set; } = "";

    public QaRunResult(bool success, string stdout, string stderr, int exitCode,
        string verdict, string judgeLayer, string reason)
    {
        Success = success;
        Stdout = stdout;
        Stderr = stderr;
        ExitCode = exitCode;
        Verdict = verdict;
        JudgeLayer = judgeLayer;
        Reason = reason;
    }
}