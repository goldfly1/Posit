using System.Diagnostics;

namespace Posit.Tools;

/// <summary>
/// Build judge — runs `dotnet build` on the generated project to check
/// compilation. Used by QA for verified modules (compile only) and
/// by C# Implementation for build-fail correction signals.
/// </summary>
public sealed class BuildJudge
{
    public async Task<(bool Success, string Output)> BuildAsync(
        string projectPath,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" --nologo 2>&1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Failed to start dotnet process");

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var output = stdout + "\n" + stderr;
            var success = process.ExitCode == 0 && output.Contains("Build succeeded");

            return (success, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Build failed: {ex.Message}");
        }
    }
}