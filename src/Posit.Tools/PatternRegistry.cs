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

    /// <summary>Path to the patterns directory (e.g. C:\Users\goldf\Posit\patterns).</summary>
    public string PatternsDirectory => _patternsDirectory;
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
            // Database I/O: use the extern portal cap (database-io.dfy declares {:extern} methods)
            if (responsibility.Contains("database") || responsibility.Contains("sql") || responsibility.Contains("repository") || responsibility.Contains("persist"))
                if (HasCSharpStub("extern-database-io"))
                    selected.Add(GetCSharpStub("extern-database-io"));

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
        if (specText.Contains("chat") || specText.Contains("message") || specText.Contains("channel") ||
            specText.Contains("messaging") || specText.Contains("presence"))
        {
            if (HasCSharpStub("chat"))
                selected.Add(GetCSharpStub("chat"));
        }
        if (specText.Contains("account") || specText.Contains("transaction") || specText.Contains("balance") ||
            specText.Contains("transfer") || specText.Contains("deposit") || specText.Contains("withdraw") || specText.Contains("banking"))
        {
            if (HasCSharpStub("banking"))
                selected.Add(GetCSharpStub("banking"));
        }
        if (specText.Contains("schedule") || specText.Contains("appointment") || specText.Contains("calendar") ||
            specText.Contains("booking") || specText.Contains("availability"))
        {
            if (HasCSharpStub("scheduling"))
                selected.Add(GetCSharpStub("scheduling"));
        }
        if (specText.Contains("search") || specText.Contains("index") || specText.Contains("catalog") ||
            specText.Contains("recommend") || specText.Contains("browse"))
        {
            if (HasCSharpStub("search"))
                selected.Add(GetCSharpStub("search"));
        }
        if (specText.Contains("workflow") || specText.Contains("approval") || specText.Contains("business process") ||
            specText.Contains("bpm"))
        {
            if (HasCSharpStub("workflow"))
                selected.Add(GetCSharpStub("workflow"));
        }
        if (specText.Contains("monitor") || specText.Contains("metric") || specText.Contains("alert") ||
            specText.Contains("health") || specText.Contains("dashboard") || specText.Contains("observability"))
        {
            if (HasCSharpStub("monitoring"))
                selected.Add(GetCSharpStub("monitoring"));
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
    /// Compose a skeleton for an io-shell component from stub files only — no pattern body.
    /// The stubs are wrapped in a module declaration so Z3 can verify the extern contracts.
    /// This gives every component a carapace (skeleton = source of truth), even io-shell.
    /// </summary>
    public string ComposeIoShellSkeleton(string componentName, IEnumerable<string> stubNames)
    {
        var stubs = stubNames.Where(HasStub).Select(GetStub).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"// Auto-composed io-shell skeleton for {componentName}");
        sb.AppendLine($"// Stubs: {(stubs.Count > 0 ? string.Join(", ", stubs.Select(s => s.Name)) : "none")}");
        sb.AppendLine();

        // Collect all dependencies
        var allDeps = new HashSet<string>();
        foreach (var stub in stubs)
            foreach (var dep in stub.Dependencies)
                allDeps.Add(dep);

        // Include dependencies (e.g., result.dfy)
        foreach (var dep in allDeps.OrderBy(d => d))
            sb.AppendLine($"include \"{dep}.dfy\"");

        if (allDeps.Count > 0)
            sb.AppendLine();

        // Wrap stubs in a module so Z3 can verify the contracts
        sb.AppendLine($"module {componentName} {{");
        foreach (var stub in stubs)
        {
            sb.AppendLine();
            sb.AppendLine($"  // ---- {stub.Name} portals ----");
            // Indent the stub source by 2 spaces to fit inside the module
            foreach (var line in stub.Source.Split('\n'))
                sb.AppendLine(string.IsNullOrWhiteSpace(line) ? line : "  " + line);
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Copy dependency .dfy files for an io-shell skeleton (stubs only, no pattern).
    /// </summary>
    public void MaterializeIoShellDependencies(IEnumerable<string> stubNames, string targetDirectory)
    {
        var stubs = stubNames.Where(HasStub).Select(GetStub).ToList();
        Directory.CreateDirectory(targetDirectory);

        var files = new List<(string sourcePath, string name)>();

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
    /// <summary>
    /// Search the variant registry for the closest match to the given component description.
    /// Uses pgvector cosine similarity to find the best pre-proven variant.
    /// Returns null if no match found or DB unavailable.
    /// </summary>
    public static VariantSearchResult? SearchVariants(string componentDescription)
    {
        try
        {
            // Get embedding from Ollama
            var embedding = GetEmbedding(componentDescription);
            if (embedding.Length == 0) return null;

            // Query pgvector for nearest match
            using var conn = new Npgsql.NpgsqlConnection(
                "Host=localhost;Port=5434;Database=shepherd;Username=shepherd;Password=shepherd");
            conn.Open();

            var vectorStr = $"[{string.Join(",", embedding.Select(v => v.ToString("G8")))}]";
            using var cmd = new Npgsql.NpgsqlCommand(@"
                SELECT id, pattern, description, source_path, vc_count,
                       1 - (embedding <=> @vec::vector) AS similarity
                FROM posit_registry.variants
                WHERE verified = true
                ORDER BY embedding <=> @vec::vector
                LIMIT 1", conn);

            cmd.Parameters.AddWithValue("@vec", vectorStr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new VariantSearchResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    (float)reader.GetDouble(5)
                );
            }
            return null;
        }
        catch
        {
            // DB unavailable — fall back to keyword matching
            return null;
        }
    }

    /// <summary>
    /// Get embedding from Ollama nomic-embed-text model.
    /// </summary>
    private static float[] GetEmbedding(string text)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = "nomic-embed-text",
                prompt = text
            });
            using var client = new System.Net.Http.HttpClient();
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = client.PostAsync("http://localhost:11434/api/embeddings", content).Result;
            if (!response.IsSuccessStatusCode) return [];
            var responseJson = response.Content.ReadAsStringAsync().Result;
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("embedding", out var embArr))
            {
                return embArr.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            }
            return [];
        }
        catch
        {
            return [];
        }
    }

    public static RegistrySuggestion Suggest(Component component)
    {
        var name = component.Name.ToLowerInvariant();
        var responsibility = component.Responsibility.ToLowerInvariant();
        var text = $"{name} {responsibility}";

        // Try semantic search first — find the closest pre-proven variant in the registry
        var semanticMatch = SearchVariants(text);
        if (semanticMatch is { Similarity: > 0.7f })
        {
            // Found a good match in the registry — use its pattern
            var pattern = semanticMatch.Pattern;
            var semanticStubs = new List<string>();

            // Still determine stubs from keywords
            if (text.Contains("file") || text.Contains("csv") || text.Contains("read") || text.Contains("write"))
                semanticStubs.Add("file-io");
            if (text.Contains("console") || text.Contains("cli") || text.Contains("print") || text.Contains("readline"))
                semanticStubs.Add("console-io");
            if (text.Contains("database") || text.Contains("sql") || text.Contains("db") || text.Contains("persist"))
                semanticStubs.Add("database-io");
            if (text.Contains("http") || text.Contains("network") || text.Contains("api") || text.Contains("socket"))
                semanticStubs.Add("network-io");
            if (text.Contains("stream") || text.Contains("chunk") || text.Contains("pipe"))
                semanticStubs.Add("stream-io");
            if (text.Contains("time") || text.Contains("random") || text.Contains("timestamp"))
                semanticStubs.Add("time-random");

            return new RegistrySuggestion(
                pattern,
                semanticStubs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                semanticMatch.SourcePath,
                semanticMatch.Description,
                semanticMatch.Similarity
            );
        }

        // Fall back to keyword matching
        var fallbackPattern = "transformer"; // default
        var stubs = new List<string>();

        if (text.Contains("parse") || text.Contains("parser"))
            fallbackPattern = "parser";
        else if (text.Contains("valid") || text.Contains("validate"))
            fallbackPattern = "validator";
        else if (text.Contains("store") || text.Contains("repository") || text.Contains("persist") || text.Contains("database"))
            fallbackPattern = "repository";
        else if (text.Contains("state") || text.Contains("transition") || text.Contains("workflow"))
            fallbackPattern = "state-machine";
        else if (text.Contains("aggregate") || text.Contains("sum") || text.Contains("count") || text.Contains("fold"))
            fallbackPattern = "aggregator";
        else if (text.Contains("build") || text.Contains("construct") || text.Contains("assemble"))
            fallbackPattern = "builder";
        else if (text.Contains("iter") || text.Contains("travers") || text.Contains("enumerate"))
            fallbackPattern = "iterator";
        else if (text.Contains("heap") || text.Contains("own") || text.Contains("repr") || text.Contains("mutable.*state"))
            fallbackPattern = "frames";
        else if (text.Contains("transform") || text.Contains("convert") || text.Contains("map"))
            fallbackPattern = "transformer";

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

        return new RegistrySuggestion(fallbackPattern, stubs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>
    /// Extract method signatures from a pattern's Dafny source. Returns the
    /// public surface — method names, parameter types, and return types.
    /// This is the data the architect needs to fill out connector specs on
    /// the carapace. The orchestrator uses it to wire deterministically.
    /// </summary>
    public static List<MethodSignature> ExtractMethodSignatures(string dafnySource)
    {
        var signatures = new List<MethodSignature>();

        // Join continuation lines: if a line starts with method/function but doesn't
        // contain both '(' and ')' on the same line, join subsequent lines until we
        // have a complete declaration.
        var lines = dafnySource.Split(['\r', '\n'], StringSplitOptions.None);
        var joinedLines = new List<string>();
        int i = 0;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if ((trimmed.StartsWith("method ", StringComparison.Ordinal) ||
                 trimmed.StartsWith("function ", StringComparison.Ordinal)) &&
                !trimmed.Contains("{:extern}"))
            {
                // Collect lines until we have balanced parens and a return type
                var sb = new StringBuilder(trimmed);
                int parenDepth = trimmed.Count(c => c == '(') - trimmed.Count(c => c == ')');
                int j = i + 1;
                while (j < lines.Length && (parenDepth > 0 || !HasReturnType(sb.ToString())))
                {
                    var nextTrimmed = lines[j].Trim();
                    if (string.IsNullOrWhiteSpace(nextTrimmed)) break;
                    // Stop if we hit the method body or another declaration
                    if (nextTrimmed.StartsWith("method ") || nextTrimmed.StartsWith("function ") ||
                        nextTrimmed.StartsWith("predicate ") || nextTrimmed.StartsWith("datatype "))
                        break;
                    sb.Append(' ').Append(nextTrimmed);
                    parenDepth += nextTrimmed.Count(c => c == '(') - nextTrimmed.Count(c => c == ')');
                    j++;
                    // Safety: don't consume more than 20 lines
                    if (j - i > 20) break;
                }
                joinedLines.Add(sb.ToString());
                i = j;
            }
            else
            {
                i++;
            }
        }

        foreach (var decl in joinedLines)
        {

            // Match: method Name(params) returns (type)
            // Match: function Name(params): type
            // (Already filtered in the join phase above — no need to re-check)

            var isFunction = decl.StartsWith("function ", StringComparison.Ordinal);
            var rest = decl[(isFunction ? 9 : 7)..].TrimStart();

            // Extract method name
            var parenStart = rest.IndexOf('(');
            if (parenStart <= 0) continue;
            var methodName = rest[..parenStart].Trim();

            // Extract parameters
            var parenEnd = FindMatchingParen(rest, parenStart);
            if (parenEnd < 0) continue;
            var paramStr = rest[(parenStart + 1)..parenEnd];

            var paramList = new List<MethodParam>();
            if (!string.IsNullOrWhiteSpace(paramStr))
            {
                foreach (var p in SplitParams(paramStr))
                {
                    var colonIdx = p.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var pName = p[..colonIdx].Trim();
                        var pType = p[(colonIdx + 1)..].Trim();
                        paramList.Add(new MethodParam(pName, pType, pType));
                    }
                }
            }

            // Extract return type
            string returnType = "void";
            string? returnDafnyType = null;
            if (isFunction)
            {
                // function Name(params): type
                var afterParams = rest[(parenEnd + 1)..].TrimStart();
                if (afterParams.StartsWith(':'))
                {
                    var typeStr = afterParams[1..].Trim();
                    // Stop at requires/ensures/decreases or newline
                    var stopIdx = typeStr.IndexOfAny([' ', '\t']);
                    if (stopIdx > 0)
                        typeStr = typeStr[..stopIdx];
                    returnType = MapDafnyTypeToCSharp(typeStr);
                    returnDafnyType = typeStr;
                }
            }
            else
            {
                // method Name(params) returns (type)
                var afterParams = rest[(parenEnd + 1)..].TrimStart();
                if (afterParams.StartsWith("returns"))
                {
                    var retParenStart = afterParams.IndexOf('(');
                    var retParenEnd = FindMatchingParen(afterParams, retParenStart);
                    if (retParenStart >= 0 && retParenEnd > retParenStart)
                    {
                        var typeStr = afterParams[(retParenStart + 1)..retParenEnd].Trim();
                        // Handle named returns: "result: seq<string>" → take type after colon
                        var colonIdx = typeStr.IndexOf(':');
                        if (colonIdx >= 0)
                            typeStr = typeStr[(colonIdx + 1)..].Trim();
                        returnType = MapDafnyTypeToCSharp(typeStr);
                        returnDafnyType = typeStr;
                    }
                }
            }

            signatures.Add(new MethodSignature(methodName, paramList.ToArray(), returnType, returnDafnyType));
        }

        return signatures;
    }

    /// <summary>
    /// Get method signatures for a named pattern. Convenience wrapper.
    /// </summary>
    public List<MethodSignature> GetPatternSignatures(string patternName)
    {
        var pattern = GetPattern(patternName);
        return ExtractMethodSignatures(pattern.Source);
    }

    /// <summary>
    /// Format a pattern's method signatures as a compact string for the
    /// architecture prompt. The architect sees this to fill out connector specs.
    /// </summary>
    public string FormatPatternSignatures(string patternName)
    {
        var sigs = GetPatternSignatures(patternName);
        if (sigs.Count == 0) return "(no public methods)";

        var sb = new StringBuilder();
        sb.AppendLine($"Pattern '{patternName}' provides these methods:");
        foreach (var sig in sigs)
        {
            var params_ = string.Join(", ", sig.Params.Select(p => $"{p.Name}: {p.DafnyType ?? p.Type}"));
            sb.AppendLine($"  - {sig.Name}({params_}) → {sig.ReturnDafnyType ?? sig.ReturnType}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Check if a declaration string already contains a return type indicator
    /// (either "returns (" for methods or ":" for functions).
    /// </summary>
    private static bool HasReturnType(string decl)
    {
        return decl.Contains("returns (") || decl.Contains("returns(") ||
               (decl.StartsWith("function ", StringComparison.Ordinal) && decl.Contains(": "));
    }

    private static int FindMatchingParen(string s, int start)
    {
        if (start < 0 || start >= s.Length || s[start] != '(') return -1;
        int depth = 0;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static List<string> SplitParams(string paramStr)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < paramStr.Length; i++)
        {
            if (paramStr[i] == '(' || paramStr[i] == '[') depth++;
            else if (paramStr[i] == ')' || paramStr[i] == ']') depth--;
            else if (paramStr[i] == ',' && depth == 0)
            {
                result.Add(paramStr[start..i].Trim());
                start = i + 1;
            }
        }
        var last = paramStr[start..].Trim();
        if (!string.IsNullOrEmpty(last)) result.Add(last);
        return result;
    }

    /// <summary>
    /// Map Dafny types to C# equivalents for the orchestrator's wiring code.
    /// </summary>
    private static string MapDafnyTypeToCSharp(string dafnyType)
    {
        var t = dafnyType.Trim();
        return t switch
        {
            "int" => "BigInteger",
            "bool" => "bool",
            "string" => "Dafny.ISequence<Dafny.Rune>",
            _ when t.StartsWith("seq<") => "Dafny.ISequence<" + MapDafnyTypeToCSharp(t[4..^1]) + ">",
            _ when t.StartsWith("set<") => "Dafny.ISet<" + MapDafnyTypeToCSharp(t[4..^1]) + ">",
            _ when t.StartsWith("map<") => "Dafny.IMap<" + t[4..^1].Split(',')[0].Trim() + ", " + MapDafnyTypeToCSharp(t[4..^1].Split(',')[1].Trim()) + ">",
            _ => t // datatypes and user-defined types pass through
        };
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
public sealed record RegistrySuggestion(string PatternName, string[] StubNames, string? BestVariantPath = null, string? BestVariantDescription = null, float SimilarityScore = 0f);
public sealed record CSharpStubEntry(string Name, string Source, string FilePath);
public sealed record VariantSearchResult(string Id, string Pattern, string Description, string SourcePath, int VcCount, float Similarity);
