using System.Diagnostics;
using System.Text;

namespace Posit.Tools;

/// <summary>
/// Docker build and run operations for the bot harness.
/// Generates Dockerfile.run (multi-stage build), builds the image,
/// and runs containers per test case.
/// </summary>
public static class BotHarnessDocker
{
    /// <summary>
    /// Generate the Dockerfile.run for multi-stage build:
    /// build stage compiles the solution, runtime stage copies output.
    /// </summary>
    public static string GenerateDockerfileRun(string cliComponentName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build");
        sb.AppendLine("WORKDIR /src");
        sb.AppendLine("COPY . .");
        sb.AppendLine("RUN dotnet build PositGenerated.sln -c Release");
        sb.AppendLine();
        sb.AppendLine("FROM mcr.microsoft.com/dotnet/runtime:10.0");
        sb.AppendLine("WORKDIR /app");
        sb.AppendLine($"COPY --from=build /src/{cliComponentName}/bin/Release/net10.0/ ./");
        // Copy test data files so the program can read them at runtime
        sb.AppendLine("COPY testdata_*.csv ./");
        sb.AppendLine("COPY testdata_*.json ./");
        sb.AppendLine("COPY testdata_*.txt ./");
        sb.AppendLine($"ENTRYPOINT [\"dotnet\", \"{cliComponentName}.dll\"]");
        return sb.ToString();
    }

    /// <summary>
    /// Build the Docker image from the temp directory.
    /// </summary>
    public static async Task<DockerResult> BuildAsync(
        string dockerPath, string contextDir, string tag, CancellationToken ct = default)
    {
        var dockerfilePath = Path.Combine(contextDir, "Dockerfile.run");
        if (!File.Exists(dockerfilePath))
            return new DockerResult(false, "Dockerfile.run not found", -1);

        // Sanitize tag: Docker tags can't start with _ or contain invalid chars
        var safeTag = new string(tag.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c : '-').ToArray());
        if (safeTag.StartsWith('-')) safeTag = "p" + safeTag[1..];
        var args = $"build --no-cache -f \"{dockerfilePath}\" -t posit-run-{safeTag.ToLowerInvariant()} \"{contextDir}\"";
        return await RunDockerAsync(dockerPath, args, ct);
    }

    private static async Task<DockerResult> RunDockerAsync(
        string dockerPath, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = dockerPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = string.IsNullOrEmpty(stderr) ? stdout : stdout + "\n" + stderr;
            return new DockerResult(process.ExitCode == 0, output, process.ExitCode);
        }
        catch (Exception ex)
        {
            return new DockerResult(false, $"Docker execution failed: {ex.Message}", -1);
        }
    }

    /// <summary>
    /// Run a container with the given input, capture stdout.
    /// If stdinInput is non-null, pipes it via stdin instead of passing as CLI arg.
    /// </summary>
    public static async Task<DockerResult> RunContainerAsync(
        string dockerPath, string tag, string cliComponentName, string input,
        CancellationToken ct = default, string? stdinInput = null)
    {
        // Sanitize tag (same as BuildAsync)
        var safeTag = new string(tag.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c : '-').ToArray());
        if (safeTag.StartsWith('-')) safeTag = "p" + safeTag[1..];
        var args = $"run --rm";
        // Add -i flag when piping stdin so the container reads from stdin
        if (stdinInput != null)
            args += " -i";
        args += $" posit-run-{safeTag.ToLowerInvariant()}";
        // Only pass input as CLI arg if no stdin piping
        if (!string.IsNullOrEmpty(input) && stdinInput == null)
            args += $" {EscapeShellArg(input)}";

        var psi = new ProcessStartInfo
        {
            FileName = dockerPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Pipe stdin if provided
        if (stdinInput != null)
            psi.RedirectStandardInput = true;

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            if (stdinInput != null)
            {
                await process.StandardInput.WriteLineAsync(stdinInput.AsMemory(), ct);
                await process.StandardInput.FlushAsync(ct);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var output = string.IsNullOrEmpty(stderr) ? stdout : stdout + "\n" + stderr;
            return new DockerResult(process.ExitCode == 0, output, process.ExitCode);
        }
        catch (Exception ex)
        {
            return new DockerResult(false, $"Docker execution failed: {ex.Message}", -1);
        }
    }

    private static string EscapeShellArg(string arg)
    {
        // Real escaping (was a no-op — worked only because every arg so far was
        // space-free; a content-bearing or spaced arg smashed into one argv).
        // ProcessStartInfo.Arguments → docker CLI → ENTRYPOINT argv: quote the
        // whole arg and double any inner quotes. Docker's argv split respects
        // the quotes; the doubled quotes survive as literal quotes in argv.
        if (string.IsNullOrEmpty(arg)) return arg;
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\t'))
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        return arg;
    }
}

public sealed record DockerResult(bool Success, string Output, int ExitCode);