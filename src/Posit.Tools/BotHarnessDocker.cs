using System.Diagnostics;
using System.Text;

namespace Posit.Tools;

/// <summary>
/// Docker build and run operations for the bot harness.
/// Generates Dockerfile.run (multi-stage build), builds the image,
/// and runs containers per test case.
/// </summary>
internal static class BotHarnessDocker
{
    /// <summary>
    /// Generate the Dockerfile.run for multi-stage build:
    /// build stage compiles the solution, runtime stage copies output + DafnyRuntime.dll.
    /// </summary>
    internal static string GenerateDockerfileRun(string cliComponentName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build");
        sb.AppendLine("WORKDIR /src");
        sb.AppendLine("COPY . .");
        sb.AppendLine("RUN dotnet build PositGenerated.sln -c Release");
        sb.AppendLine();
        sb.AppendLine("FROM mcr.microsoft.com/dotnet/runtime:10.0");
        sb.AppendLine("WORKDIR /app");
        // Copy from per-project output dir (includes runtimeconfig.json)
        sb.AppendLine($"COPY --from=build /src/{cliComponentName}/bin/Release/net10.0/ ./");
        // Copy DafnyRuntime.dll from build context (not build stage)
        sb.AppendLine("COPY DafnyRuntime/DafnyRuntime.dll ./");
        // Copy test data files so the program can read them at runtime
        sb.AppendLine("COPY testdata_*.csv ./");
        sb.AppendLine("COPY testdata_*.json ./");
        sb.AppendLine($"ENTRYPOINT [\"dotnet\", \"{cliComponentName}.dll\"]");
        return sb.ToString();
    }

    /// <summary>
    /// Build the Docker image from the temp directory.
    /// </summary>
    internal static async Task<DockerResult> BuildAsync(
        string dockerPath, string contextDir, string tag, CancellationToken ct = default)
    {
        var dockerfilePath = Path.Combine(contextDir, "Dockerfile.run");
        if (!File.Exists(dockerfilePath))
            return new DockerResult(false, "Dockerfile.run not found");

        var args = $"build --no-cache -f \"{dockerfilePath}\" -t posit-run-{tag.ToLowerInvariant()} \"{contextDir}\"";
        return await RunDockerAsync(dockerPath, args, ct);
    }

    /// <summary>
    /// Run a container with the given input, capture stdout.
    /// </summary>
    internal static async Task<DockerResult> RunContainerAsync(
        string dockerPath, string tag, string cliComponentName, string input, CancellationToken ct = default)
    {
        // Pass input via stdin if non-empty, otherwise just run
        var args = $"run --rm posit-run-{tag.ToLowerInvariant()}";
        if (!string.IsNullOrEmpty(input))
            args += $" {EscapeShellArg(input)}";

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
            return new DockerResult(process.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return new DockerResult(false, $"Docker execution failed: {ex.Message}");
        }
    }

    private static string EscapeShellArg(string arg)
    {
        // Docker passes args to the ENTRYPOINT directly. Don't add quotes —
        // they become part of the argument value, causing FileNotFoundException.
        return arg;
    }
}

internal sealed record DockerResult(bool Success, string Output);