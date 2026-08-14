using System.Text;
using System.Text.Json;
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

    public BotHarness(ArtifactRepository repo, string? dockerPath = null)
    {
        _repo = repo;
        _dockerPath = dockerPath ?? "docker";
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

        var buildResult = await BotHarnessDocker.BuildAsync(_dockerPath, tempDir, sessionId.Value, ct);
        if (!buildResult.Success) return Fail($"Docker build failed: {buildResult.Output}");

        var testCases = ExtractTestCases(cliComponent, testSuite);
        var results = new List<TestCaseResult>();

        foreach (var tc in testCases)
        {
            var runResult = await BotHarnessDocker.RunContainerAsync(
                _dockerPath, sessionId.Value, cliComponent.Name, tc.Input, ct);
            results.Add(new TestCaseResult(tc.Id, tc.Name, runResult.Success, runResult.Output,
                tc.ExpectedBehavior, CompareOutput(runResult.Output, tc.ExpectedBehavior)));
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
        return actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
            || (actual.Contains("exit code 0", StringComparison.OrdinalIgnoreCase)
                && expected.Contains("exit code 0", StringComparison.OrdinalIgnoreCase));
    }

    internal static T? Deserialize<T>(byte[] payloadJson) where T : class
    {
        var json = Encoding.UTF8.GetString(payloadJson);
        return JsonSerializer.Deserialize<T>(json, PositJson.Options);
    }

    private static BotHarnessResult Fail(string error) => new(false, [], null, error);
}

public sealed record BotHarnessResult(bool Success, TestCaseResult[] Results, string? TempDir, string? Error);
public sealed record TestCaseResult(string Id, string Name, bool Ran, string Output, string Expected, bool Matches);
internal sealed record TestCaseInfo(string Id, string Name, string Input, string ExpectedBehavior);