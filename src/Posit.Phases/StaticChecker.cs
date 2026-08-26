namespace Posit.Phases;

using System.Text;

/// <summary>
/// Static checker — scans generated C# code for known error patterns before
/// sending it to dotnet build. Catches errors instantly and free, saving a
/// compiler call + a model correction call.
///
/// C#-direct pipeline: only C# rules. Checks that generated C# doesn't
/// accidentally use Dafny runtime types (ISequence, BigRational, etc.).
/// </summary>
public static class StaticChecker
{
    /// <summary>
    /// Check C# source for known error patterns before dotnet build.
    /// </summary>
    public static List<StaticIssue> CheckCSharp(string csharpSource)
    {
        var issues = new List<StaticIssue>();

        // 1. Dafny runtime types — should not appear in C#-direct output
        if (csharpSource.Contains("Dafny.") || csharpSource.Contains("ISequence<") ||
            csharpSource.Contains("BigRational") || csharpSource.Contains("UnicodeFromString"))
        {
            issues.Add(new StaticIssue(
                "dafny-runtime-type",
                "Dafny runtime type detected (Dafny., ISequence, BigRational, UnicodeFromString). " +
                "Use native C# types only: string, int, bool, string[], etc."));
        }

        // 2. Missing namespace declaration
        if (!csharpSource.Contains("namespace "))
        {
            issues.Add(new StaticIssue(
                "missing-namespace",
                "No namespace declaration found. All C# files must have a namespace."));
        }

        // 3. Missing class/interface declaration
        if (!csharpSource.Contains("class ") && !csharpSource.Contains("interface "))
        {
            issues.Add(new StaticIssue(
                "missing-type-declaration",
                "No class or interface declaration found."));
        }

        // 4. Markdown fences left in output
        if (csharpSource.Contains("```"))
        {
            issues.Add(new StaticIssue(
                "markdown-fences",
                "Markdown code fences (```) found in C# source. Remove them — output raw C# only."));
        }

        return issues;
    }

    /// <summary>
    /// Format issues as a feedback string for the model.
    /// </summary>
    public static string FormatIssues(List<StaticIssue> issues)
    {
        if (issues.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("═══ STATIC CHECKER — C# ISSUES (fix before compilation) ═══");
        foreach (var issue in issues)
        {
            sb.AppendLine($"  ❌ [{issue.RuleId}] {issue.Message}");
        }
        sb.AppendLine("═══ END STATIC CHECKER ═══");
        return sb.ToString();
    }

    /// <summary>
    /// Classify a set of static issues into an error class for escalation detection.
    /// </summary>
    public static string ClassifyStaticIssue(List<StaticIssue> issues)
    {
        if (issues.Count == 0) return "static-unknown";
        return issues[0].RuleId;
    }
}

/// <summary>
/// A single static check issue — a rule violation with a specific message.
/// </summary>
public record StaticIssue(string RuleId, string Message);