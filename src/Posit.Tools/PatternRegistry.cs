using System.Text;
using System.Text.RegularExpressions;

namespace Posit.Tools;

/// <summary>
/// Loads patterns (Dafny .dfy), Dafny stubs, and C# stub templates from the
/// patterns/ directory. Provides skeleton composition, dependency materialization,
/// and pattern suggestion.
/// </summary>
public sealed partial class PatternRegistry
{
    private readonly Dictionary<string, PatternEntry> _patterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StubEntry> _stubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CSharpStubEntry> _csharpStubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _patternsRoot;

    public PatternRegistry(string patternsRoot)
    {
        _patternsRoot = patternsRoot;
        LoadPatterns();
        LoadStubs();
        LoadCSharpStubs();
    }

    public static PatternRegistry Create(string repoRoot) =>
        new(Path.Combine(repoRoot, "patterns"));

    public bool HasPattern(string name) => _patterns.ContainsKey(name);
    public PatternEntry? GetPattern(string name) => _patterns.TryGetValue(name, out var p) ? p : null;
    public bool HasCSharpStub(string name) => _csharpStubs.ContainsKey(name);
    public CSharpStubEntry? GetCSharpStub(string name) => _csharpStubs.TryGetValue(name, out var s) ? s : null;
    public IReadOnlyCollection<PatternEntry> GetAllPatterns() => _patterns.Values;
    public IReadOnlyCollection<StubEntry> GetAllStubs() => _stubs.Values;
    public IReadOnlyCollection<CSharpStubEntry> GetAllCSharpStubs() => _csharpStubs.Values;

    /// <summary>
    /// Select C# stubs for a component based on responsibility keywords and stub names.
    /// NEVER returns io-console-program — uses console-io as fallback.
    /// </summary>
    public List<CSharpStubEntry> SelectCSharpStubs(string responsibility, string[] stubNames)
    {
        var result = new List<CSharpStubEntry>();
        var respLower = (responsibility ?? "").ToLowerInvariant();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in stubNames)
        {
            var resolved = name.Equals("io-console-program", StringComparison.OrdinalIgnoreCase) ? "console-io" : name;
            if (_csharpStubs.TryGetValue(resolved, out var stub) && used.Add(resolved))
                result.Add(stub);
        }

        foreach (var stub in _csharpStubs.Values)
        {
            if (used.Contains(stub.Name) || stub.Name == "io-console-program") continue;
            if (stub.AutoBindKeywords == null) continue;
            foreach (var kw in stub.AutoBindKeywords)
            {
                if (!string.IsNullOrEmpty(kw) && respLower.Contains(kw.ToLowerInvariant()))
                {
                    if (used.Add(stub.Name)) result.Add(stub);
                    break;
                }
            }
        }

        if (result.Count == 0 && (respLower.Contains("console") || respLower.Contains("cli")))
        {
            if (_csharpStubs.TryGetValue("console-io", out var consoleStub))
                result.Add(consoleStub);
        }

        return result;
    }

    private void LoadPatterns()
    {
        foreach (var file in Directory.GetFiles(_patternsRoot, "*.dfy"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "dafny-reference-card") continue;
            var content = File.ReadAllText(file);
            var (patternName, responsibility) = ParsePatternHeader(content);
            _patterns[name] = new PatternEntry(name, patternName, responsibility, content,
                ExtractKeywords(responsibility), content.Contains("include \"result.dfy\""));
        }
        // Load cut-outs (domain-specific pre-cut Dafny modules)
        var cutOutsDir = Path.Combine(_patternsRoot, "cut-outs");
        if (Directory.Exists(cutOutsDir))
        {
            foreach (var file in Directory.GetFiles(cutOutsDir, "*.dfy"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var content = File.ReadAllText(file);
                var (patternName, responsibility) = ParsePatternHeader(content);
                _patterns[name] = new PatternEntry(name, patternName, responsibility, content,
                    ExtractKeywords(responsibility), content.Contains("include \"result.dfy\""), IsCutOut: true);
            }
        }
    }

    private void LoadStubs()
    {
        var dir = Path.Combine(_patternsRoot, "stubs");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.dfy"))
            _stubs[Path.GetFileNameWithoutExtension(file)] = new StubEntry(
                Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
    }

    private void LoadCSharpStubs()
    {
        var dir = Path.Combine(_patternsRoot, "csharp-stubs");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.cs.template"))
        {
            var name = Path.GetFileNameWithoutExtension(file).Replace(".cs", "");
            _csharpStubs[name] = new CSharpStubEntry(name, File.ReadAllText(file), ParseCSharpStubAutoBind(File.ReadAllText(file)));
        }
    }

    private static (string patternName, string responsibility) ParsePatternHeader(string content)
    {
        var patternName = "";
        var responsibility = "";
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.StartsWith("// Pattern:")) patternName = t.Replace("// Pattern:", "").Trim();
            else if (t.StartsWith("// responsibility:")) { responsibility = t.Replace("// responsibility:", "").Trim(); break; }
        }
        return (patternName, responsibility);
    }

    private static string[] ParseCSharpStubAutoBind(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("// Auto-bound when spec mentions:"))
                return t.Replace("// Auto-bound when spec mentions:", "").Trim()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return [];
    }

    private static string[] ExtractKeywords(string responsibility)
    {
        if (string.IsNullOrEmpty(responsibility)) return [];
        return responsibility.Split(new[] { ' ', ',', '-', '—', '→', '/' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

public sealed record PatternEntry(string Name, string PatternName, string Responsibility, string Body, string[] Keywords, bool IncludesResult, bool IsCutOut = false);
public sealed record StubEntry(string Name, string Body);
public sealed record CSharpStubEntry(string Name, string Template, string[] AutoBindKeywords);
internal sealed record MethodSigInfo(string Name, string Params, string ReturnType);