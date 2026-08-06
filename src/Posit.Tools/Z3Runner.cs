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
    /// Runs `dafny translate cs` on a verified .dfy file. Returns the
    /// translated C# source code, or null on failure.
    /// </summary>
    public async Task<string?> TranslateToCSharpAsync(
        string dafnyFilePath,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _dafnyExecutable,
                Arguments = BuildTranslateArguments(dafnyFilePath),
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

            return stdout;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] dafny translate cs failed: {ex.Message}");
            return null;
        }
    }

    private string BuildVerifyArguments(string dafnyPath) =>
        $"verify \"{dafnyPath}\" --solver-path \"{_z3SolverPath}\"" +
        $" --verification-time-limit {_verificationTimeoutSeconds}" +
        " --standard-libraries";

    private string BuildTranslateArguments(string dafnyPath) =>
        $"translate cs \"{dafnyPath}\" --solver-path \"{_z3SolverPath}\"";
}