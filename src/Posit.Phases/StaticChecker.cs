namespace Posit.Phases;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Static checker — scans generated code for known error patterns before
/// sending it to Z3 (Dafny) or dotnet build (C#). Catches errors instantly
/// and free, saving a compiler/prover call + a model correction call.
///
/// Same tool, two rule sets:
/// - Dafny: function ban, C#-isms, missing invariants/decreases
/// - C#: wrong API (BigRational.Parse), dtor_ mismatch, type conversion gaps
/// </summary>
public static class StaticChecker
{
    // ── Dafny rules ──────────────────────────────────────────────────────────

    /// <summary>
    /// Check Dafny source for known error patterns before Z3 verification.
    /// Returns a list of issues — each with a specific, actionable message
    /// the model can fix. Empty list = no issues found, safe to send to Z3.
    /// </summary>
    public static List<StaticIssue> CheckDafny(string dafnySource)
    {
        var issues = new List<StaticIssue>();

        // 1. Function ban: any `function` (not `function method`) with a body
        //    containing while/var/:=/return is a guaranteed Z3 failure.
        var functionMatches = Regex.Matches(dafnySource,
            @"(?<!datatype\s)\bfunction\b\s+\w+\s*\(",
            RegexOptions.IgnoreCase);
        foreach (Match m in functionMatches)
        {
            // Check if this function has a body with imperative constructs
            var funcStart = m.Index;
            var funcEnd = dafnySource.IndexOf('{', funcStart);
            if (funcEnd < 0) funcEnd = dafnySource.Length;
            var funcBody = funcEnd < dafnySource.Length
                ? ExtractBlock(dafnySource, funcEnd)
                : "";

            if (funcBody.Contains("while") || funcBody.Contains("var ") ||
                funcBody.Contains(":=") || funcBody.Contains("return"))
            {
                issues.Add(new StaticIssue(
                    "function-with-imperative-body",
                    $"Line {GetLineNumber(dafnySource, m.Index)}: 'function' with loops/assignment. Change to 'method'. Functions are pure expressions — no while, var, :=, or return."));
            }
            else
            {
                // Even pure functions should be methods per our decision
                issues.Add(new StaticIssue(
                    "function-used",
                    $"Line {GetLineNumber(dafnySource, m.Index)}: 'function' used. ALWAYS use 'method' instead. Change 'function' to 'method'."));
            }
        }

        // 2. C#-ism: (char) cast
        var charCastMatches = Regex.Matches(dafnySource, @"\(char\)");
        foreach (Match m in charCastMatches)
        {
            issues.Add(new StaticIssue(
                "cs-ism-char-cast",
                $"Line {GetLineNumber(dafnySource, m.Index)}: C# cast '(char)'. Use Dafny 'char(...)' instead."));
        }

        // 3. C#-ism: C-style for loop
        var forLoopMatches = Regex.Matches(dafnySource, @"for\s*\(\s*\w+\s*=\s*0\s*;");
        foreach (Match m in forLoopMatches)
        {
            issues.Add(new StaticIssue(
                "cs-ism-for-loop",
                $"Line {GetLineNumber(dafnySource, m.Index)}: C-style for loop. Use Dafny 'for i := 0 to n-1' or 'while' with invariant."));
        }

        // 4. C#-ism: new string[|s|] (should be new char[|s|])
        if (dafnySource.Contains("new string["))
        {
            issues.Add(new StaticIssue(
                "cs-ism-new-string-array",
                "C# array syntax 'new string[...]'. Dafny arrays need element type: 'new char[|s|]'."));
        }

        // 5. C#-ism: map[K]V instead of map[K, V]
        var mapMatches = Regex.Matches(dafnySource, @"map\s*\[\s*\w+\s*\]\s*\w");
        foreach (Match m in mapMatches)
        {
            // Check if it's not map[K, V] (comma present)
            var afterBracket = dafnySource.Substring(m.Index, Math.Min(30, dafnySource.Length - m.Index));
            if (!afterBracket.Contains(","))
            {
                issues.Add(new StaticIssue(
                    "cs-ism-map-syntax",
                    $"Line {GetLineNumber(dafnySource, m.Index)}: map syntax. Use 'map[K, V]' with comma, not 'map[K]V'."));
            }
        }

        // 6. C#-ism: seq[T] instead of seq<T>
        var seqMatches = Regex.Matches(dafnySource, @"seq\s*\[\s*\w+\s*\]");
        foreach (Match m in seqMatches)
        {
            issues.Add(new StaticIssue(
                "cs-ism-seq-syntax",
                $"Line {GetLineNumber(dafnySource, m.Index)}: seq syntax. Use 'seq<T>' with angle brackets, not 'seq[T]'."));
        }

        // 7. C#-ism: rbracket issues — [char(...)] or [expr] used as seq literal
        //    Dafny uses [x] for seq literals but the parser can choke on complex expressions inside.
        //    Common pattern: result + [char('0' + digit)] → use seq<char> construction
        var bracketExprMatches = Regex.Matches(dafnySource, @"\[\s*char\s*\(");
        foreach (Match m in bracketExprMatches)
        {
            issues.Add(new StaticIssue(
                "rbracket-char-expr",
                $"Line {GetLineNumber(dafnySource, m.Index)}: '[char(...)]' may cause parser error. Use '[char(...)]' only for simple expressions, or build the seq with a helper method instead."));
        }

        // 8. while loop without invariant
        var whileMatches = Regex.Matches(dafnySource, @"\bwhile\b\s*\(");
        foreach (Match m in whileMatches)
        {
            // Find the block after the while condition
            var blockStart = dafnySource.IndexOf('{', m.Index);
            if (blockStart >= 0)
            {
                var blockEnd = dafnySource.IndexOf('}', blockStart);
                if (blockEnd >= 0)
                {
                    var block = dafnySource.Substring(blockStart, blockEnd - blockStart);
                    if (!block.Contains("invariant"))
                    {
                        issues.Add(new StaticIssue(
                            "missing-invariant",
                            $"Line {GetLineNumber(dafnySource, m.Index)}: while loop without 'invariant' clause. Add invariants that hold on entry and are preserved by the loop body."));
                    }
                }
            }
        }

        // 8. Assignment with = instead of :=
        var assignMatches = Regex.Matches(dafnySource, @"^\s*\w+\s*=\s*[^=]", RegexOptions.Multiline);
        foreach (Match m in assignMatches)
        {
            // Exclude == (comparison) and >= <= != (comparisons)
            var line = dafnySource.Substring(m.Index, Math.Min(40, dafnySource.Length - m.Index));
            if (!line.Contains("==") && !line.Contains(">=") && !line.Contains("<=") &&
                !line.Contains("!=") && !line.Contains("=>"))
            {
                issues.Add(new StaticIssue(
                    "cs-ism-assignment",
                    $"Line {GetLineNumber(dafnySource, m.Index)}: assignment with '='. Dafny uses ':=' for assignment."));
            }
        }

        return issues;
    }

    // ── C# rules ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Check C# Wire.cs source for known error patterns before dotnet build.
    /// Accepts the translated C# type definitions so it can verify property names.
    /// </summary>
    public static List<StaticIssue> CheckCSharp(string csharpSource, string? translatedTypeDefinitions = null)
    {
        var issues = new List<StaticIssue>();

        // 1. BigRational.Parse — doesn't exist
        if (csharpSource.Contains("BigRational.Parse"))
        {
            issues.Add(new StaticIssue(
                "wrong-api-bigrational-parse",
                "'BigRational.Parse' does not exist. Use 'new Dafny.BigRational(double.Parse(...))' instead."));
        }

        // 2. dtor_ property mismatch — check against actual translated types
        if (!string.IsNullOrWhiteSpace(translatedTypeDefinitions))
        {
            // Extract all dtor_ property names from the translated types
            var validDtors = new HashSet<string>();
            var dtorMatches = Regex.Matches(translatedTypeDefinitions, @"dtor_\w+");
            foreach (Match m in dtorMatches)
                validDtors.Add(m.Value);

            // Check all dtor_ references in Wire.cs against the valid set
            var wireDtorMatches = Regex.Matches(csharpSource, @"dtor_\w+");
            foreach (Match m in wireDtorMatches)
            {
                if (!validDtors.Contains(m.Value))
                {
                    issues.Add(new StaticIssue(
                        "dtor-mismatch",
                        $"'{m.Value}' does not exist in the translated C# types. Valid properties: {string.Join(", ", validDtors)}."));
                }
            }
        }

        // 3. Missing 'using Dafny;' directive
        if (!csharpSource.Contains("using Dafny;") && csharpSource.Contains("Dafny."))
        {
            issues.Add(new StaticIssue(
                "missing-using-dafny",
                "References 'Dafny.' types but missing 'using Dafny;' directive."));
        }

        // 4. ISequence<Rune> used as string without conversion
        if (csharpSource.Contains("ISequence<Rune>") && csharpSource.Contains(".ToString()") &&
            !csharpSource.Contains("UnicodeFromString") && !csharpSource.Contains("Select(r => (char)"))
        {
            issues.Add(new StaticIssue(
                "missing-type-conversion",
                "ISequence<Rune> used with .ToString() — this returns the object name, not the string content. Use '.Select(r => (char)r.Value).ToArray()' to convert to string."));
        }

        // 5. string used where ISequence<Rune> expected (or vice versa) without conversion
        // Check for direct assignment between string and ISequence without UnicodeFromString
        var stringToSeqMatches = Regex.Matches(csharpSource, @"=\s*""[^""]*""\s*;");
        // This is a heuristic — would need the method signatures to be precise

        return issues;
    }

    /// <summary>
    /// Format issues as a feedback string for the model.
    /// </summary>
    public static string FormatIssues(List<StaticIssue> issues, string language)
    {
        if (issues.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine($"═══ STATIC CHECKER — {language} ISSUES (fix before compilation) ═══");
        foreach (var issue in issues)
        {
            sb.AppendLine($"  ❌ [{issue.RuleId}] {issue.Message}");
        }
        sb.AppendLine("═══ END STATIC CHECKER ═══");
        return sb.ToString();
    }

    /// <summary>
    /// Classify a set of static issues into an error class for escalation detection.
    /// Returns the first issue's rule ID (or "static-unknown").
    /// </summary>
    public static string ClassifyStaticIssue(List<StaticIssue> issues)
    {
        if (issues.Count == 0) return "static-unknown";
        // Map to the same error classes used by ClassifyError
        var ruleId = issues[0].RuleId;
        return ruleId switch
        {
            "function-with-imperative-body" or "function-used" => "while-in-function",
            "cs-ism-for-loop" => "cs-ism-for-loop",
            "cs-ism-char-cast" => "cs-ism-char-cast",
            "cs-ism-map-syntax" or "cs-ism-seq-syntax" or "cs-ism-new-string-array" => "cs-ism-generic-syntax",
            "cs-ism-assignment" => "cs-ism-assignment",
            "missing-invariant" => "missing-invariant",
            _ => ruleId
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ExtractBlock(string source, int braceStart)
    {
        var depth = 0;
        var sb = new StringBuilder();
        for (var i = braceStart; i < source.Length; i++)
        {
            sb.Append(source[i]);
            if (source[i] == '{') depth++;
            if (source[i] == '}') { depth--; if (depth == 0) break; }
        }
        return sb.ToString();
    }

    private static int GetLineNumber(string source, int charIndex)
    {
        return source.Substring(0, Math.Min(charIndex, source.Length)).Split('\n').Length;
    }
}

/// <summary>
/// A single static check issue — a rule violation with a specific message.
/// </summary>
public record StaticIssue(string RuleId, string Message);