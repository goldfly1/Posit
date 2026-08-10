using System.Text;
using Posit.Contracts.Artifacts;

namespace Posit.Tools;

/// <summary>
/// The object registry — the pre-cut stone quarry for Posit.
///
/// Column A: Patterns (hull shapes). Proven Dafny skeletons for common
/// software modules: parser, validator, repository, state-machine, etc.
///
/// Column B: Stubs (oar holes). Pre-cut {:extern} I/O portals for file,
/// stream, console, database, network, and time/random operations.
///
/// The architect does not invent module shapes. It classifies components and
/// selects from this registry. The Imp fills logic inside the pre-cut portals.
/// Pass 2 (C# Implementation) plugs real implementations into the {:extern} stubs.
///
/// "2 from column A, 3 from column B, and a bowlful of non-Dafny caps to cover
/// the stubs." — every software model type is represented here.
/// </summary>
public sealed class PatternRegistry
{
    private readonly string _patternsDirectory;
    private readonly string _stubsDirectory;
    private readonly Dictionary<string, PatternEntry> _patterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StubEntry> _stubs = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _csharpStubsDirectory;
    private readonly Dictionary<string, CSharpStubEntry> _csharpStubs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, CSharpStubEntry> CSharpStubs => _csharpStubs;

    public bool HasCSharpStub(string name) => _csharpStubs.ContainsKey(name);

    public CSharpStubEntry GetCSharpStub(string name) => _csharpStubs.TryGetValue(name, out var s)
        ? s
        : throw new KeyNotFoundException($"C# stub '{name}' not found in registry. Available: {string.Join(", ", _csharpStubs.Keys)}");

    /// <summary>
    /// Select the appropriate C# stub template(s) for a component based on the architecture contract.
    /// Returns empty if the component is pure Dafny with no {:extern} stubs needing a C# cap.
    /// </summary>
    public IReadOnlyList<CSharpStubEntry> SelectCSharpStubs(Component component)
    {
        var selected = new List<CSharpStubEntry>();
        var name = component.Name;
        var classification = component.Classification;
        var tech = component.Tech?.ToLowerInvariant() ?? "";
        var responsibility = component.Responsibility?.ToLowerInvariant() ?? "";

        // Io-shell components get implementation files from the rack.
        if (classification == ModuleClassification.IoShell || tech == "c#")
        {
            // Match by responsibility keywords
            if (responsibility.Contains("database") || responsibility.Contains("sql") || responsibility.Contains("repository") || responsibility.Contains("persist"))
                if (HasCSharpStub("io-database-repository"))
                    selected.Add(GetCSharpStub("io-database-repository"));

            if (component.StubNames?.Any(s => s.Contains("console", StringComparison.OrdinalIgnoreCase)) == true ||
                responsibility.Contains("console") || responsibility.Contains("cli") || responsibility.Contains("command-line") ||
                name.Contains("CLI", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Console", StringComparison.OrdinalIgnoreCase))
                if (HasCSharpStub("io-console-program"))
                    selected.Add(GetCSharpStub("io-console-program"));

            if (component.StubNames?.Any(s => s.Contains("file", StringComparison.OrdinalIgnoreCase)) == true ||
                responsibility.Contains("file") || responsibility.Contains("read") || responsibility.Contains("write") ||
                name.Contains("File", StringComparison.OrdinalIgnoreCase) || name.Contains("Reader", StringComparison.OrdinalIgnoreCase) || name.Contains("Writer", StringComparison.OrdinalIgnoreCase))
                if (HasCSharpStub("file-io"))
                    selected.Add(GetCSharpStub("file-io"));

            if (component.StubNames?.Any(s => s.Contains("stream", StringComparison.OrdinalIgnoreCase)) == true ||
                responsibility.Contains("stream"))
                if (HasCSharpStub("stream-io"))
                    selected.Add(GetCSharpStub("stream-io"));

            if (component.StubNames?.Any(s => s.Contains("network", StringComparison.OrdinalIgnoreCase)) == true ||
                responsibility.Contains("http") || responsibility.Contains("network") || responsibility.Contains("api"))
                if (HasCSharpStub("network-io"))
                    selected.Add(GetCSharpStub("network-io"));

            // Generic fallback: if no specific stub matched, give it a console-program shell
            // so the carapace enforcement doesn't abort. The architect can refine later.
            if (selected.Count == 0)
            {
                if (HasCSharpStub("io-console-program"))
                    selected.Add(GetCSharpStub("io-console-program"));
                else if (HasCSharpStub("file-io"))
                    selected.Add(GetCSharpStub("file-io"));
            }
        }

        // Dafny components with {:extern} stubs need matching C# partial-class implementations.
        // The C# stub files are named the same as the Dafny stub files (e.g., "file-io", "stream-io").
        foreach (var stubName in component.StubNames ?? [])
        {
            if (HasCSharpStub(stubName))
                selected.Add(GetCSharpStub(stubName));
        }

        // Fallback for dafny components: if no C# stub matched, give them a file-io shell
        // so the carapace enforcement doesn't abort on dafny components with externs.
        if (selected.Count == 0 && (classification == ModuleClassification.Dafny || classification == ModuleClassification.Mixed))
        {
            if (HasCSharpStub("file-io"))
                selected.Add(GetCSharpStub("file-io"));
        }

        // Domain-specific stubs: match spec keywords to domain stub templates
        var specText = (component.Responsibility ?? "").ToLowerInvariant();
        if (specText.Contains("product") || specText.Contains("cart") || specText.Contains("order") ||
            specText.Contains("payment") || specText.Contains("inventory") || specText.Contains("shipping") ||
            specText.Contains("tax") || specText.Contains("commerce") || specText.Contains("marketplace"))
        {
            if (HasCSharpStub("ecommerce"))
                selected.Add(GetCSharpStub("ecommerce"));
        }
        if (specText.Contains("pipeline") || specText.Contains("build") || specText.Contains("deploy") ||
            specText.Contains("artifact") || specText.Contains("ci/cd") || specText.Contains("job") && specText.Contains("step"))
        {
            if (HasCSharpStub("cicd"))
                selected.Add(GetCSharpStub("cicd"));
        }
        if (specText.Contains("patient") || specText.Contains("medical") || specText.Contains("prescription") ||
            specText.Contains("lab") || specText.Contains("insurance") || specText.Contains("hipaa"))
        {
            if (HasCSharpStub("healthcare"))
                selected.Add(GetCSharpStub("healthcare"));
        }

        return selected.Distinct().ToList();
    }

    /// <summary>
    /// Render a C# stub template for a component by substituting placeholders.
    /// </summary>
    public static string RenderCSharpStub(CSharpStubEntry stub, string componentName)
    {
        return stub.Source
            .Replace("{{ComponentName}}", componentName)
            .Replace("{{componentName}}", componentName);
    }

    public PatternRegistry(string patternsDirectory)
    {
        if (!Directory.Exists(patternsDirectory))
            throw new DirectoryNotFoundException($"Pattern registry directory not found: {patternsDirectory}");

        _patternsDirectory = patternsDirectory;
        _stubsDirectory = Path.Combine(patternsDirectory, "stubs");
        _csharpStubsDirectory = Path.Combine(patternsDirectory, "csharp-stubs");
        Load();
    }

    public IReadOnlyDictionary<string, PatternEntry> Patterns => _patterns;
    public IReadOnlyDictionary<string, StubEntry> Stubs => _stubs;

    public PatternEntry GetPattern(string name) => _patterns.TryGetValue(name, out var p)
        ? p
        : throw new KeyNotFoundException($"Pattern '{name}' not found in registry. Available: {string.Join(", ", _patterns.Keys)}");

    public bool HasPattern(string name) => _patterns.ContainsKey(name);

    public StubEntry GetStub(string name) => _stubs.TryGetValue(name, out var s)
        ? s
        : throw new KeyNotFoundException($"Stub '{name}' not found in registry. Available: {string.Join(", ", _stubs.Keys)}");

    public bool HasStub(string name) => _stubs.ContainsKey(name);

    /// <summary>
    /// Compose a Dafny skeleton for a component by combining a pattern with
    /// zero or more stub attachments. The result is a complete .dfy file content
    /// that includes all required dependencies.
    /// </summary>
    public string ComposeSkeleton(string componentName, string patternName, IEnumerable<string> stubNames)
    {
        var pattern = GetPattern(patternName);
        var stubs = stubNames.Select(GetStub).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"// Auto-composed skeleton for {componentName}");
        sb.AppendLine($"// Pattern: {patternName}");
        sb.AppendLine($"// Stubs: {(stubs.Count > 0 ? string.Join(", ", stubs.Select(s => s.Name)) : "none")}");
        sb.AppendLine();

        // Include result.dfy if any dependency needs it
        var allDeps = new HashSet<string>();
        foreach (var dep in pattern.Dependencies)
            allDeps.Add(dep);

        foreach (var stub in stubs)
            foreach (var dep in stub.Dependencies)
                allDeps.Add(dep);

        foreach (var dep in allDeps.OrderBy(d => d))
        {
            sb.AppendLine($"include \"{dep}.dfy\"");
        }

        if (allDeps.Count > 0)
            sb.AppendLine();

        // Substitute component-specific placeholders into the pattern template.
        var patternSource = pattern.Source
            .Replace("{{ComponentName}}", componentName)
            .Replace("{{componentName}}", componentName);

        sb.AppendLine(patternSource);

        foreach (var stub in stubs)
        {
            sb.AppendLine();
            sb.AppendLine($"// ---- {stub.Name} portals ----");
            sb.AppendLine(stub.Source);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Copy all dependency .dfy files required by the composed skeleton into the
    /// target directory. Call this after writing the composed skeleton to disk.
    /// </summary>
    public void MaterializeDependencies(string componentName, string patternName, IEnumerable<string> stubNames, string targetDirectory)
    {
        var pattern = GetPattern(patternName);
        var stubs = stubNames.Select(GetStub).ToList();
        Directory.CreateDirectory(targetDirectory);

        var files = new List<(string sourcePath, string name)> { (pattern.FilePath, $"{pattern.Name}.dfy") };
        foreach (var dep in pattern.Dependencies)
        {
            if (_patterns.TryGetValue(dep, out var depPattern))
                files.Add((depPattern.FilePath, $"{depPattern.Name}.dfy"));
        }

        foreach (var stub in stubs)
        {
            files.Add((stub.FilePath, $"{stub.Name}.dfy"));
            foreach (var dep in stub.Dependencies)
            {
                if (_patterns.TryGetValue(dep, out var depPattern))
                    files.Add((depPattern.FilePath, $"{depPattern.Name}.dfy"));
                if (_stubs.TryGetValue(dep, out var depStub))
                    files.Add((depStub.FilePath, $"{depStub.Name}.dfy"));
            }
        }

        foreach (var (sourcePath, name) in files.Distinct())
        {
            var target = Path.Combine(targetDirectory, name);
            if (!File.Exists(target))
                File.Copy(sourcePath, target);
        }
    }

    /// <summary>
    /// Suggest registry patterns and stubs for a component based on its name,
    /// responsibility, and classification. This is deterministic guidance for
    /// the architect model, not a hard rule.
    /// </summary>
    public static RegistrySuggestion Suggest(Component component)
    {
        var name = component.Name.ToLowerInvariant();
        var responsibility = component.Responsibility.ToLowerInvariant();
        var text = $"{name} {responsibility}";

        var pattern = "transformer"; // default
        var stubs = new List<string>();

        if (text.Contains("parse") || text.Contains("parser"))
            pattern = "parser";
        else if (text.Contains("valid") || text.Contains("validate"))
            pattern = "validator";
        else if (text.Contains("store") || text.Contains("repository") || text.Contains("persist") || text.Contains("database"))
            pattern = "repository";
        else if (text.Contains("state") || text.Contains("transition") || text.Contains("workflow"))
            pattern = "state-machine";
        else if (text.Contains("aggregate") || text.Contains("sum") || text.Contains("count") || text.Contains("fold"))
            pattern = "aggregator";
        else if (text.Contains("build") || text.Contains("construct") || text.Contains("assemble"))
            pattern = "builder";
        else if (text.Contains("iter") || text.Contains("travers") || text.Contains("enumerate"))
            pattern = "iterator";
        else if (text.Contains("heap") || text.Contains("own") || text.Contains("repr") || text.Contains("mutable.*state"))
            pattern = "frames";
        else if (text.Contains("transform") || text.Contains("convert") || text.Contains("map"))
            pattern = "transformer";

        if (text.Contains("file") || text.Contains("csv") || text.Contains("read") || text.Contains("write"))
            stubs.Add("file-io");
        if (text.Contains("console") || text.Contains("cli") || text.Contains("print") || text.Contains("readline"))
            stubs.Add("console-io");
        if (text.Contains("database") || text.Contains("sql") || text.Contains("db") || text.Contains("persist"))
            stubs.Add("database-io");
        if (text.Contains("http") || text.Contains("network") || text.Contains("api") || text.Contains("socket"))
            stubs.Add("network-io");
        if (text.Contains("stream") || text.Contains("chunk") || text.Contains("pipe"))
            stubs.Add("stream-io");
        if (text.Contains("time") || text.Contains("random") || text.Contains("timestamp"))
            stubs.Add("time-random");

        return new RegistrySuggestion(pattern, stubs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void Load()
    {
        foreach (var file in Directory.EnumerateFiles(_patternsDirectory, "*.dfy"))
        {
            if (Path.GetDirectoryName(file) != _patternsDirectory)
                continue; // skip stubs in the top-level scan

            var name = Path.GetFileNameWithoutExtension(file);
            var source = File.ReadAllText(file);
            var deps = ExtractIncludes(source);
            _patterns[name] = new PatternEntry(name, source, file, deps);
        }

        if (Directory.Exists(_stubsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_stubsDirectory, "*.dfy"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var source = File.ReadAllText(file);
                var deps = ExtractIncludes(source);
                _stubs[name] = new StubEntry(name, source, file, deps);
            }
        }

        if (Directory.Exists(_csharpStubsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_csharpStubsDirectory, "*.cs.template"))
            {
                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                var source = File.ReadAllText(file);
                _csharpStubs[name] = new CSharpStubEntry(name, source, file);
            }
        }
    }

    private static string[] ExtractIncludes(string source)
    {
        var includes = new List<string>();
        foreach (var line in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("include \"", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('"') + 1;
                var end = trimmed.LastIndexOf('"');
                if (end > start)
                {
                    var include = trimmed[start..end];
                    if (include.EndsWith(".dfy", StringComparison.OrdinalIgnoreCase))
                        include = include[..^4];
                    includes.Add(include);
                }
            }
        }
        return includes.ToArray();
    }
}

public sealed record PatternEntry(string Name, string Source, string FilePath, string[] Dependencies);
public sealed record StubEntry(string Name, string Source, string FilePath, string[] Dependencies);
public sealed record RegistrySuggestion(string PatternName, string[] StubNames);
public sealed record CSharpStubEntry(string Name, string Source, string FilePath);
