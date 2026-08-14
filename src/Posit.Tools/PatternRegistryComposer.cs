using System.Text;
using System.Text.RegularExpressions;

namespace Posit.Tools;

/// <summary>
/// Skeleton composition, dependency materialization, and pattern suggestion
/// for the PatternRegistry.
/// </summary>
public sealed partial class PatternRegistry
{
    /// <summary>
    /// Compose a Dafny skeleton file from a pattern and its stub dependencies.
    /// </summary>
    public string ComposeSkeleton(string patternName, string[] stubNames, string moduleName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Module: {moduleName}");
        sb.AppendLine($"// Pattern: {patternName}");
        if (stubNames.Length > 0) sb.AppendLine($"// Stubs: {string.Join(", ", stubNames)}");
        sb.AppendLine();

        if (_patterns.TryGetValue(patternName, out var pattern) && pattern.IncludesResult)
            sb.AppendLine("include \"result.dfy\"");

        sb.AppendLine(pattern?.Body ?? "// ERROR: pattern not found");

        foreach (var stubName in stubNames)
        {
            if (_stubs.TryGetValue(stubName, out var stub))
            {
                sb.AppendLine();
                sb.AppendLine($"// === Stub: {stubName} ===");
                sb.AppendLine(stub.Body);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Compose the C# stub for an io-shell component from C# templates.
    /// Substitutes {{ComponentName}} with the actual component name.
    /// Returns C# code (not Dafny) — used by CSharpImplementationPhase.
    /// </summary>
    public string ComposeIoShellSkeleton(string[] stubNames, string moduleName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Module: {moduleName} (io-shell)");
        sb.AppendLine($"// Stubs: {string.Join(", ", stubNames)}");
        sb.AppendLine();

        foreach (var stubName in stubNames)
        {
            if (_csharpStubs.TryGetValue(stubName, out var stub))
            {
                sb.AppendLine($"// === Stub: {stubName} ===");
                // Substitute {{ComponentName}} with the actual module name
                var content = stub.Template.Replace("{{ComponentName}}", moduleName);
                sb.AppendLine(content);
                sb.AppendLine();
            }
            else if (_stubs.TryGetValue(stubName, out var dafnyStub))
            {
                // Fallback: if no C# template, emit a comment (Dafny stubs are not C#)
                sb.AppendLine($"// === Stub: {stubName} (Dafny only — no C# template) ===");
                sb.AppendLine($"// {dafnyStub.Body.Split('\n')[0]}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Materialize Dafny dependency files (includes like result.dfy) to a staging path.
    /// </summary>
    public void MaterializeDependencies(string stagingPath, string patternName)
    {
        Directory.CreateDirectory(stagingPath);
        if (_patterns.TryGetValue(patternName, out var pattern) && pattern.IncludesResult)
        {
            var resultPath = Path.Combine(stagingPath, "result.dfy");
            if (_patterns.TryGetValue("result", out var resultPattern))
                File.WriteAllText(resultPath, resultPattern.Body);
        }
    }

    /// <summary>
    /// Materialize Dafny stub files for an io-shell component.
    /// </summary>
    public void MaterializeIoShellDependencies(string stagingPath, string[] stubNames)
    {
        Directory.CreateDirectory(stagingPath);
        foreach (var stubName in stubNames)
        {
            if (_stubs.TryGetValue(stubName, out var stub))
                File.WriteAllText(Path.Combine(stagingPath, $"{stubName}.dfy"), stub.Body);
        }
    }

    /// <summary>
    /// Get method signatures from a pattern as a formatted string for prompts.
    /// </summary>
    public string GetPatternSignatures(string patternName)
    {
        if (!_patterns.TryGetValue(patternName, out var pattern))
            return $"// Pattern '{patternName}' not found";
        return FormatPatternSignatures(patternName, ExtractMethodSignatures(pattern.Body));
    }

    internal static List<MethodSigInfo> ExtractMethodSignatures(string dafnyBody)
    {
        var sigs = new List<MethodSigInfo>();
        var regex = new Regex(
            @"(?:method|function)\s+(\w+)\s*\(([^)]*)\)(?:\s*returns\s*\(([^)]*)\))?(?:\s*:\s*(\w+))?",
            RegexOptions.Multiline);

        foreach (Match m in regex.Matches(dafnyBody))
        {
            var name = m.Groups[1].Value;
            var paramsStr = m.Groups[2].Value.Trim();
            var returnType = m.Groups[3].Success ? m.Groups[3].Value.Trim()
                           : m.Groups[4].Success ? m.Groups[4].Value.Trim() : "";
            sigs.Add(new MethodSigInfo(name, paramsStr, returnType));
        }
        return sigs;
    }

    internal static string FormatPatternSignatures(string patternName, List<MethodSigInfo> sigs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pattern: {patternName}");
        foreach (var s in sigs)
        {
            var ret = string.IsNullOrEmpty(s.ReturnType) ? "" : $" → {s.ReturnType}";
            sb.AppendLine($"  - {s.Name}({s.Params}){ret}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Suggest patterns matching a responsibility description.
    /// </summary>
    public List<PatternEntry> Suggest(string responsibility)
    {
        var respLower = (responsibility ?? "").ToLowerInvariant();
        var results = new List<(PatternEntry p, int score)>();

        foreach (var p in _patterns.Values)
        {
            if (p.Name == "result" || p.Name == "dafny-reference-card") continue;
            var score = p.Keywords.Count(kw => !string.IsNullOrEmpty(kw) && respLower.Contains(kw.ToLowerInvariant()));
            if (score > 0) results.Add((p, score));
        }

        return results.OrderByDescending(r => r.score).ThenBy(r => r.p.Name)
            .Select(r => r.p).ToList();
    }
}