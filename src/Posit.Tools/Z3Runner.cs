using System.Diagnostics;

namespace Posit.Tools;

/// <summary>
/// Runs the Dafny verifier (Z3) on .dfy files and translates verified
/// Dafny source to C#. This is the deterministic core of the Dafny-first
/// pipeline — no model calls, no guessing. Z3 proves or it doesn't.
/// </summary>
public sealed class Z3Runner
{
    private readonly string _dafnyExecutable;
    private readonly string _z3SolverPath;
    private readonly int _verificationTimeoutSeconds;

    /// <summary>
    /// Creates a Z3Runner. Defaults are pulled from environment variables
    /// or fall back to the known install locations on this machine.
    /// </summary>
    /// <param name="dafnyExecutable">Path to the dafny executable (or "dafny" if on PATH).</param>
    /// <param name="z3SolverPath">Path to z3.exe for --solver-path flag.</param>
    /// <param name="verificationTimeoutSeconds">Z3 per-method time limit.</param>
    public Z3Runner(
        string? dafnyExecutable = null,
        string? z3SolverPath = null,
        int verificationTimeoutSeconds = 30)
    {
        _dafnyExecutable = dafnyExecutable
            ?? Environment.GetEnvironmentVariable("DAFNY_EXE")
            ?? "dafny";
        _z3SolverPath = z3SolverPath
            ?? Environment.GetEnvironmentVariable("DAFNY_Z3_PATH")
            ?? @"C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe";
        _verificationTimeoutSeconds = verificationTimeoutSeconds;
    }

    /// <summary>
    /// Runs `dafny verify` on a .dfy file. Returns true if Z3 verified
    /// with 0 errors, false otherwise. Output contains the full stdout+stderr
    /// for correction signals on failure.
    /// </summary>
    public async Task<(bool Verified, string Output)> VerifyAsync(
        string dafnyFilePath,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _dafnyExecutable,
                Arguments = BuildVerifyArguments(dafnyFilePath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Failed to start dafny process");

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var output = stdout + "\n" + stderr;
            var verified = output.Contains("verified, 0 errors") && !output.Contains("errors]");
            return (verified, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Dafny execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs `dafny translate cs` on a verified .dfy file. Writes the translated
    /// C# to a file in the staging directory and returns the file path.
    /// Uses --include-runtime to embed the Dafny runtime in the output.
    /// </summary>
    public async Task<string?> TranslateToCSharpAsync(
        string dafnyFilePath,
        CancellationToken ct = default)
    {
        try
        {
            var outputPath = Path.ChangeExtension(dafnyFilePath, ".cs");
            var psi = new ProcessStartInfo
            {
                FileName = _dafnyExecutable,
                Arguments = $"{BuildTranslateArguments(dafnyPath: dafnyFilePath)} --output \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"[Posit] dafny translate cs failed: {stderr[..Math.Min(200, stderr.Length)]}");
                return null;
            }

            // If the file wasn't created, fall back to stdout
            if (!File.Exists(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, stdout, ct);
            }

            return outputPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] dafny translate cs failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Staging directory for .dfy files and generated C#. Persists across
    /// phases so Imp can find skeleton files from a prior phase. Uses
    /// .posit/staging/ relative to the working directory.
    /// </summary>
    public static string StagingDirectory =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging");

    /// <summary>
    /// Ensures the staging directory exists.
    /// </summary>
    public static void EnsureStagingDirectory()
    {
        Directory.CreateDirectory(StagingDirectory);
    }

    /// <summary>
    /// Creates a staging path for a .dfy file.
    /// </summary>
    public static string GetDafnyStagingPath(string moduleName)
    {
        EnsureStagingDirectory();
        var safeName = moduleName.Replace(".", "_").Replace("/", "_").Replace("\\", "_");
        return Path.Combine(StagingDirectory, $"{safeName}.dfy");
    }

    private string BuildVerifyArguments(string dafnyPath) =>
        $"verify \"{dafnyPath}\" --solver-path \"{_z3SolverPath}\"" +
        $" --verification-time-limit {_verificationTimeoutSeconds}" +
        " --standard-libraries";

    private string BuildTranslateArguments(string dafnyPath) =>
        $"translate cs \"{dafnyPath}\" --solver-path \"{_z3SolverPath}\" --include-runtime";
}