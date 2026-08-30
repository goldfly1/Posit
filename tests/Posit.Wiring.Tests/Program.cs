using System.Diagnostics;
using Posit.Contracts.Artifacts;
using Posit.Phases;

/// <summary>
/// WiringGenerator corpus gate — every Wire.cs shape the trials generate must
/// COMPILE before it ever reaches Docker or a fixer. Deterministic code should
/// never need an LLM fixer (Phase C1, gauntlet-results-and-factory-road.md).
///
/// Each case builds a full temp project (interfaces + impls + stub + Wire.cs)
/// and runs `dotnet build`. If Wire.cs doesn't compile, this fails loudly.
/// </summary>

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var tempRoot = Path.Combine(Path.GetTempPath(), "posit-wiring-tests");
Directory.CreateDirectory(tempRoot);
var failures = new List<string>();
var passCount = 0;

foreach (var (name, buildCase) in Corpus.All)
{
    var caseDir = Path.Combine(tempRoot, name);
    if (Directory.Exists(caseDir)) Directory.Delete(caseDir, recursive: true);
    Directory.CreateDirectory(caseDir);
    try
    {
        var project = buildCase();       // case writes files, returns proj dir
        var (ok, output) = DotNetBuild(project);
        if (ok)
        {
            Interlocked.Increment(ref passCount);
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            failures.Add($"{name}: {FirstErrors(output)}");
            Console.WriteLine($"  [FAIL] {name}\n{FirstErrors(output)}");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: EX {ex.Message}");
        Console.WriteLine($"  [FAIL] {name} — {ex.Message}");
    }
    finally
    {
        try { Directory.Delete(caseDir, recursive: true); } catch { }
    }
}

Console.WriteLine($"\n{passCount}/{Corpus.All.Count} corpus cases compile");
if (failures.Count > 0)
{
    Console.Error.WriteLine("WIRING CORPUS FAIL");
    return 1;
}
Console.WriteLine("WIRING CORPUS PASS");

// ── Gate tests (ContractFidelityChecker, ContractScanner, TypeChainChecker) ──
// TODO: GateTests.cs needs constructor fixes — work in progress
// Console.WriteLine("\n── Gate Tests ──");
// var gateResult = GateTests.Run();
// if (gateResult != 0) return gateResult;
return 0;

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Posit.sln")) && !Directory.Exists(Path.Combine(dir.FullName, "patterns")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
}

static (bool ok, string output) DotNetBuild(string projDir)
{
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{projDir}\" --nologo -v q",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    p.WaitForExit(120_000);
    return (p.ExitCode == 0, output);
}

static string FirstErrors(string output) =>
    string.Join("\n", output.Split('\n').Where(l => l.Contains("error")).Take(4));