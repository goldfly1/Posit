using System.Diagnostics;
using System.Text;

namespace Posit.Tools;

/// <summary>
/// Runs Dafny verification (Z3) and Dafny-to-C# translation.
/// Post-processes translated C# to extract only the module namespace,
/// discarding all DafnyRuntime boilerplate.
/// </summary>
public sealed class Z3Runner
{
    private readonly string _dafnyExecutable;
    private readonly string _z3SolverPath;
    private readonly int _verificationTimeoutSeconds;

    public static string StagingDirectory => Path.Combine(Path.GetTempPath(), "posit-dafny-staging");

    public Z3Runner(string dafnyExecutable, string z3SolverPath, int verificationTimeoutSeconds = 120)
    {
        _dafnyExecutable = dafnyExecutable;
        _z3SolverPath = z3SolverPath;
        _verificationTimeoutSeconds = verificationTimeoutSeconds;
    }

    public static string GetDafnyStagingPath(string moduleName) =>
        Path.Combine(StagingDirectory, moduleName);

    public static void EnsureStagingDirectory() => Directory.CreateDirectory(StagingDirectory);

    public async Task<Z3VerificationResult> VerifyAsync(string dafnyPath, CancellationToken ct = default)
    {
        EnsureStagingDirectory();
        var args = $"verify \"{dafnyPath}\""
                 + $" --solver-path:\"{_z3SolverPath}\""
                 + $" --verification-time-limit:{_verificationTimeoutSeconds}"
                 + " --allow-warnings";

        var (exitCode, stdout, stderr) = await RunDafnyAsync(args, ct);
        var success = exitCode == 0 && !stdout.Contains("verification errors", StringComparison.OrdinalIgnoreCase);
        return new Z3VerificationResult(success, exitCode, stdout, stderr, ParseVerificationErrors(stdout, stderr));
    }

    public async Task<Z3TranslationResult> TranslateAsync(
        string dafnyPath, string moduleName, CancellationToken ct = default)
    {
        EnsureStagingDirectory();

        var args = $"translate cs \"{dafnyPath}\""
                 + " --no-verify"
                 + " --allow-external-contracts"
                 + " --allow-warnings"
                 + " --test-assumptions Externs"
                 + " --translate-standard-library:false";

        var (exitCode, stdout, stderr) = await RunDafnyAsync(args, ct);

        if (exitCode != 0 && !stdout.Contains("namespace _module", StringComparison.Ordinal))
            return new Z3TranslationResult(false, exitCode, stdout, stderr, null);

        var rawCsharp = stdout;
        if (string.IsNullOrEmpty(rawCsharp) || !rawCsharp.Contains("namespace _module"))
        {
            var csPath = Path.ChangeExtension(dafnyPath, ".cs");
            if (File.Exists(csPath))
                rawCsharp = await File.ReadAllTextAsync(csPath, ct);
        }

        if (string.IsNullOrEmpty(rawCsharp))
            return new Z3TranslationResult(false, exitCode, stdout, stderr, null);

        return new Z3TranslationResult(true, 0, stdout, stderr, PostProcessTranslation(rawCsharp, moduleName));
    }

    internal static string PostProcessTranslation(string rawCsharp, string moduleName)
    {
        var processed = rawCsharp.Replace("namespace _module {", $"namespace _module_{moduleName} {{");
        processed = processed.Replace("_module.", $"_module_{moduleName}.");

        var nsStart = processed.IndexOf($"namespace _module_{moduleName}", StringComparison.Ordinal);
        if (nsStart < 0)
            return $"// ERROR: could not find namespace _module_{moduleName} in translated output\n{rawCsharp}";

        var braceStart = processed.IndexOf('{', nsStart);
        if (braceStart < 0)
            return $"// ERROR: could not find opening brace for namespace _module_{moduleName}\n{rawCsharp}";

        var braceEnd = FindMatchingBrace(processed, braceStart);
        if (braceEnd < 0)
            return $"// ERROR: unmatched braces for namespace _module_{moduleName}\n{rawCsharp}";

        var namespaceBlock = processed.Substring(nsStart, braceEnd - nsStart + 1);

        return $"using System;\nusing System.Numerics;\nusing System.Collections;\n\n{namespaceBlock}";
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunDafnyAsync(
        string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _dafnyExecutable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string[] ParseVerificationErrors(string stdout, string stderr)
    {
        var errors = new List<string>();
        var combined = stdout + "\n" + stderr;
        foreach (var line in combined.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("error:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("verification error", StringComparison.OrdinalIgnoreCase))
                errors.Add(trimmed);
        }
        return [.. errors];
    }
}

public sealed record Z3VerificationResult(bool Success, int ExitCode, string Stdout, string Stderr, string[] Errors);

public sealed record Z3TranslationResult(bool Success, int ExitCode, string Stdout, string Stderr, string? CleanCsharp);