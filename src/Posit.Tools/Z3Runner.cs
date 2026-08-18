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
                 + " --allow-warnings"
                 + " --verbose";

        var (exitCode, stdout, stderr) = await RunDafnyAsync(args, ct);
        var success = exitCode == 0 && !stdout.Contains("verification errors", StringComparison.OrdinalIgnoreCase);
        var errors = ParseVerificationErrors(stdout, stderr);
        // Translate opaque CoCo parser errors into plain-English hints
        errors = TranslateOpaqueErrors(errors, dafnyPath);
        return new Z3VerificationResult(success, exitCode, stdout, stderr, errors);
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

        // Some Dafny translations put the real code in "namespace {ModuleName}" not
        // "namespace _module_{ModuleName}". If the _module block is empty, fall back.
        if (!namespaceBlock.Contains("public static") && !namespaceBlock.Contains("__default"))
        {
            var altStart = processed.IndexOf($"namespace {moduleName} {{", StringComparison.Ordinal);
            if (altStart >= 0)
            {
                var altBraceStart = processed.IndexOf('{', altStart);
                var altBraceEnd = FindMatchingBrace(processed, altBraceStart);
                if (altBraceEnd > altBraceStart)
                {
                    var altBlock = processed.Substring(altStart, altBraceEnd - altStart + 1);
                    if (altBlock.Contains("public static") || altBlock.Contains("__default"))
                        namespaceBlock = altBlock;
                }
            }
        }

        return $"using System;\nusing System.Numerics;\nusing System.Collections;\n\n{namespaceBlock}";
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        // Dafny's C# translation can have unbalanced braces (goto labels produce
        // extra closing braces). So we can't rely on depth==0 alone.
        // Strategy: find "} // end of namespace _module" marker (Dafny always emits this as the last namespace).
        // Some translations have multiple nested namespaces (Dafny, FrequencyAggregator, _module).
        var endMarker = text.IndexOf("} // end of namespace _module", openIndex, StringComparison.Ordinal);
        if (endMarker >= 0) return endMarker;
        // Fallback: find last } that brings depth to 0 or below
        var depth = 0;
        var lastZero = -1;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth <= 0) lastZero = i; }
        }
        return lastZero;
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

    /// <summary>
    /// Translate opaque CoCo parser errors ("invalid UnaryExpression", "this symbol not
    /// expected") into plain-English hints that the model can act on. The CoCo parser
    /// generates generic "invalid X" messages when it can't parse something, with no
    /// explanation of the actual language rule violated. We inspect the Dafny source at
    /// the error location to determine the real cause and append a helpful hint.
    /// </summary>
    private static string[] TranslateOpaqueErrors(string[] errors, string dafnyPath)
    {
        if (errors.Length == 0) return errors;

        string[]? sourceLines = null;
        try { sourceLines = File.ReadAllLines(dafnyPath); }
        catch { return errors; } // can't read source, return as-is

        var translated = new List<string>();
        foreach (var err in errors)
        {
            translated.Add(err);

            // Detect generic CoCo parse errors — these all have the ID p_generic_syntax_error
            // and come in flavors like "invalid UnaryExpression", "rbracket expected",
            // "this symbol not expected", etc. The --verbose flag reveals the ID.
            var isOpaque = err.Contains("p_generic_syntax_error", StringComparison.OrdinalIgnoreCase)
                || err.Contains("this symbol not expected", StringComparison.OrdinalIgnoreCase)
                || (err.Contains("invalid ", StringComparison.OrdinalIgnoreCase)
                    && err.Contains("Expression", StringComparison.OrdinalIgnoreCase))
                || err.Contains("rbracket expected", StringComparison.OrdinalIgnoreCase)
                || err.Contains("lbracket expected", StringComparison.OrdinalIgnoreCase)
                || err.Contains("rbrace expected", StringComparison.OrdinalIgnoreCase)
                || err.Contains("lbrace expected", StringComparison.OrdinalIgnoreCase)
                || err.Contains("identifier expected", StringComparison.OrdinalIgnoreCase)
                || err.Contains("expected but found", StringComparison.OrdinalIgnoreCase);

            if (!isOpaque) continue;

            // Extract line number from error: "path(line,col): Error: ..."
            var lineNum = ExtractLineNumber(err);
            if (lineNum == null || lineNum < 1 || lineNum > sourceLines.Length) continue;

            var errorLine = sourceLines[lineNum.Value - 1].Trim();
            var hint = GenerateHint(err, errorLine, lineNum.Value, sourceLines);
            if (hint != null)
                translated.Add($"  HINT: {hint}");
        }
        return [.. translated];
    }

    private static int? ExtractLineNumber(string error)
    {
        // Format: "path(line,col): Error: ..."
        var parenStart = error.IndexOf('(');
        if (parenStart < 0) return null;
        var comma = error.IndexOf(',', parenStart);
        if (comma < 0) return null;
        var lineStr = error.AsSpan(parenStart + 1, comma - parenStart - 1);
        if (int.TryParse(lineStr, out var line)) return line;
        return null;
    }

    /// <summary>
    /// Inspect the error line and surrounding context to generate a plain-English hint.
    /// The CoCo parser says "invalid UnaryExpression" but the real issue is usually a
    /// language rule violation — imperative code inside a function, missing keywords, etc.
    /// </summary>
    private static string? GenerateHint(string errText, string errorLine, int lineNum, string[] sourceLines)
    {
        // Check: is this line inside a function (not a method)?
        // Walk backward from the error line to find the enclosing function/method declaration.
        var enclosingDecl = "unknown";
        for (var i = lineNum - 2; i >= 0 && i >= lineNum - 50; i--)
        {
            var l = sourceLines[i].Trim();
            if (l.StartsWith("function ") || l.StartsWith("function {"))
            { enclosingDecl = "function"; break; }
            if (l.StartsWith("method ") || l.StartsWith("method {"))
            { enclosingDecl = "method"; break; }
            if (l.StartsWith("lemma "))
            { enclosingDecl = "lemma"; break; }
            if (l.StartsWith("predicate "))
            { enclosingDecl = "predicate"; break; }
        }

        // Pattern: while/var-reassignment/return inside a function
        if (enclosingDecl == "function")
        {
            if (errorLine.StartsWith("while ") || errorLine.Contains("while "))
                return "You used a 'while' loop inside a 'function'. Functions must be pure expressions — no loops, no mutable variable reassignment, no 'return' statements. Either change 'function' to 'method' (which allows imperative code), or rewrite the logic as a recursive expression.";
            if (errorLine.StartsWith("var ") && errorLine.Contains(':') && !errorLine.Contains("=>"))
                return "You used a mutable 'var' binding inside a 'function'. Functions must be pure expressions. Use 'var x := E; rest' let-binding style (no reassignment), or change 'function' to 'method'.";
            if (errorLine.StartsWith("return") || errorLine.Contains("return "))
                return "You used 'return' inside a 'function'. Functions return their body as an expression — no 'return' statement needed. Either change 'function' to 'method', or write the body as a single expression.";
            return "This line is inside a 'function', which must be a pure expression (no loops, no mutable assignment, no return statements). Either change 'function' to 'method', or rewrite as a pure expression.";
        }

        // Pattern: "this symbol not expected" — usually JSON or prose written as Dafny
        if (errorLine.StartsWith("{") || errorLine.StartsWith("["))
            return "The file starts with JSON or brackets, not Dafny code. Output ONLY raw Dafny source starting with 'module'.";

        // Pattern: bracket errors — usually wrong syntax for map/array/seq types
        if (errText.Contains("rbracket expected", StringComparison.OrdinalIgnoreCase))
        {
            if (errorLine.Contains("map["))
                return "Invalid map syntax. Dafny maps use a comma between key and value types: 'map[string, int]' not 'map[string]int'. Also, Dafny maps are immutable — use 'map[string, int] := ...' for initialization, or use a series of updates.";
            if (errorLine.Contains("seq[") || errorLine.Contains("set["))
                return "Invalid sequence/set syntax. Dafny uses 'seq<string>' not 'seq[string]'. Angle brackets for generic types, square brackets only for indexing.";
            if (errorLine.Contains("[") && errorLine.Contains("]"))
                return "Bracket mismatch on this line. Check that all '[' have matching ']' and that type parameters use angle brackets (seq<T>) not square brackets (seq[T]).";
            return "A closing bracket ']' was expected but not found. Check the syntax on this line — common causes: wrong map syntax (use 'map[K, V]' with comma), wrong generic syntax (use 'seq<T>' not 'seq[T]'), or missing closing bracket.";
        }

        // Pattern: while without invariant
        if (errorLine.StartsWith("while "))
            return "While loops in Dafny require 'invariant' and 'decreases' clauses. Example: while i < n invariant 0 <= i <= n decreases n - i { ... }";

        // Generic fallback for opaque errors
        return $"The parser could not understand this line. Dafny's parser gives 'invalid X' errors when it encounters syntax it doesn't recognize. Check that this line uses valid Dafny syntax — see the reference card for correct forms.";
    }
}

public sealed record Z3VerificationResult(bool Success, int ExitCode, string Stdout, string Stderr, string[] Errors);

public sealed record Z3TranslationResult(bool Success, int ExitCode, string Stdout, string Stderr, string? CleanCsharp);