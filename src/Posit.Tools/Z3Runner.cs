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
    /// Uses default translation (no --include-runtime) — the Dafny runtime is provided by the shared
    /// Posit.DafnyRuntime project, not embedded in each translated file.
    /// </summary>
    public async Task<string?> TranslateToCSharpAsync(
        string dafnyFilePath,
        string moduleName,
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
                    $"[Posit] dafny translate cs failed (exit {process.ExitCode}):\nSTDOUT:\n{stdout[..Math.Min(500, stdout.Length)]}\nSTDERR:\n{stderr[..Math.Min(500, stderr.Length)]}");
                return null;
            }

            // dafny translate cs may produce a directory or multiple files.
            // Prefer the explicitly requested output, then look for generated .cs files.
            if (!File.Exists(outputPath))
            {
                var dir = Path.GetDirectoryName(dafnyFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var generated = Directory.GetFiles(dir, "*.cs")
                        .OrderBy(f => f)
                        .ToList();
                    if (generated.Count == 1)
                    {
                        outputPath = generated[0];
                    }
                    else if (generated.Count > 1)
                    {
                        var moduleNamed = generated.FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f).Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                        outputPath = moduleNamed ?? generated[0];
                        Console.Error.WriteLine($"[Posit] dafny translate cs produced {generated.Count} files; using {outputPath}");
                    }
                }
            }

            // If still no file, write stdout as fallback
            if (!File.Exists(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, stdout, ct);
            }

            // Post-process: rename `namespace _module` to `namespace _module_{moduleName}`
            // so that Wire.cs can use `using _module_{moduleName};` to reference the
            // correct module. Without this, every Dafny module produces `namespace _module`
            // and they collide when referenced from C#.
            // Also update internal fully-qualified references from `_module.` to `_module_{moduleName}.`
            // Also strip the Dafny runtime helpers (FuncExtensions, ArrayHelpers, DafnyAssembly)
            // which are provided by DafnyRuntime.dll — including them causes CS0101 duplicates.
            if (File.Exists(outputPath))
            {
                var csharpContent = await File.ReadAllTextAsync(outputPath, ct);
                // Rename the namespace declaration
                var renamed = csharpContent.Replace("namespace _module {", $"namespace _module_{moduleName} {{");
                // Rename internal fully-qualified references to _module.X → _module_{moduleName}.X
                // Be careful: only replace `_module.` (with the dot), not `_module_` or `_module{`
                renamed = renamed.Replace("_module.", $"_module_{moduleName}.");
                // Strip Dafny runtime helpers that conflict with DafnyRuntime.dll:
                // - [assembly: DafnyAssembly.DafnySourceAttribute(...)] — multi-line attribute
                // - internal static class FuncExtensions { ... } — entire class
                // - namespace Dafny { ... } — entire namespace block (contains ArrayHelpers)
                renamed = StripDafnyRuntimeHelpers(renamed);
                if (renamed != csharpContent)
                {
                    await File.WriteAllTextAsync(outputPath, renamed, ct);
                    Console.Error.WriteLine($"[Posit] dafny translate cs — renamed namespace + stripped runtime helpers in {Path.GetFileName(outputPath)}");
                }
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
        $"translate cs \"{dafnyPath}\" --no-verify --allow-external-contracts --allow-warnings --translate-standard-library:false";

    /// <summary>
    /// Strip Dafny runtime helpers from translated C# that are already provided
    /// by DafnyRuntime.dll. Including them causes CS0101 (duplicate definitions).
    /// Strips:
    /// - [assembly: DafnyAssembly.DafnySourceAttribute(...)] — multi-line attribute
    /// - internal static class FuncExtensions { ... } — entire class block
    /// - namespace Dafny { ... } — entire namespace block (contains ArrayHelpers)
    /// </summary>
    private static string StripDafnyRuntimeHelpers(string content)
    {
        // Strip the DafnyAssembly attribute (may span multiple lines)
        content = System.Text.RegularExpressions.Regex.Replace(content,
            @"\[assembly:\s+DafnyAssembly\.DafnySourceAttribute\([^)]*\)\]",
            "// [DafnyAssembly attribute stripped — provided by DafnyRuntime.dll]",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Strip the FuncExtensions class (internal static class FuncExtensions { ... })
        content = StripBlock(content, "internal static class FuncExtensions");

        // Strip the Dafny namespace (namespace Dafny { ... } — contains ArrayHelpers)
        content = StripNamespace(content, "Dafny");

        return content;
    }

    /// <summary>
    /// Strip a class/block by finding its declaration and matching braces.
    /// </summary>
    private static string StripBlock(string content, string declarationMarker)
    {
        var idx = content.IndexOf(declarationMarker, StringComparison.Ordinal);
        if (idx < 0) return content;

        // Find the opening brace
        var braceStart = content.IndexOf('{', idx);
        if (braceStart < 0) return content;

        // Match braces to find the end
        var depth = 0;
        for (int i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            if (content[i] == '}') depth--;
            if (depth == 0)
            {
                // Include the line before (comment) and the closing brace line
                var lineStart = content.LastIndexOf('\n', idx);
                lineStart = lineStart < 0 ? 0 : lineStart + 1;
                return content[..lineStart] + "// [stripped: " + declarationMarker + " — provided by DafnyRuntime.dll]\n" + content[(i + 1)..];
            }
        }
        return content;
    }

    /// <summary>
    /// Strip a namespace block by finding its declaration and matching braces.
    /// </summary>
    private static string StripNamespace(string content, string nsName)
    {
        var marker = $"namespace {nsName} {{";
        var idx = content.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            // Try without space before brace
            marker = $"namespace {nsName}{{";
            idx = content.IndexOf(marker, StringComparison.Ordinal);
        }
        if (idx < 0) return content;

        var braceStart = content.IndexOf('{', idx);
        if (braceStart < 0) return content;

        var depth = 0;
        for (int i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            if (content[i] == '}') depth--;
            if (depth == 0)
            {
                var lineStart = content.LastIndexOf('\n', idx);
                lineStart = lineStart < 0 ? 0 : lineStart + 1;
                return content[..lineStart] + "// [stripped: namespace " + nsName + " — provided by DafnyRuntime.dll]\n" + content[(i + 1)..];
            }
        }
        return content;
    }
}