using System.Text;
using Posit.Contracts.Artifacts;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Generates Wire.cs files — one per component with connections.
/// Uses TranslatedCSharpScanner to read the actual translated C# and wire
/// against real method signatures — no guessing from pattern files.
///
/// This is DETERMINISTIC — no model call, no judgment.
/// </summary>
public sealed class WiringGenerator
{
    private readonly PatternRegistry _registry;
    private readonly TranslatedCSharpScanner _scanner;
    private Dictionary<string, List<TranslatedCSharpScanner.CsMethod>> _scannedMethods = new(StringComparer.OrdinalIgnoreCase);

    public WiringGenerator(PatternRegistry registry)
    {
        _registry = registry;
        _scanner = new TranslatedCSharpScanner();
    }

    /// <summary>
    /// Generate wiring files for all components with connections.
    /// Scans the actual translated C# files first, then wires against reality.
    /// </summary>
    public List<SourceCodeFile> Generate(
        ArchitectureContract arch,
        List<(string ModuleName, string CSharpPath)> translatedFiles)
    {
        // Scan the actual translated C# — this is the key change.
        // We see what Dafny actually emitted, not what we guess from patterns.
        _scannedMethods = _scanner.ScanAll(translatedFiles);

        // Also scan io-shell stub files from the patterns directory
        ScanIoShellStubs(arch);
        var result = new List<SourceCodeFile>();
        var components = arch.Components;
        if (components.Length == 0) return result;

        var componentByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
            componentByName[c.Name] = c;

        var cliComponent = FindCliComponent(components);
        if (cliComponent is null) return result;

        var translatedNames = new HashSet<string>(
            translatedFiles.Select(t => t.ModuleName),
            StringComparer.OrdinalIgnoreCase);

        var componentsWithConnections = components
            .Where(c => c.Connections?.Length > 0 && c.MethodSignatures?.Length > 0)
            .ToList();

        foreach (var comp in componentsWithConnections)
        {
            var isCli = string.Equals(comp.Name, cliComponent.Name, StringComparison.OrdinalIgnoreCase);
            var wireFile = GenerateComponentWiring(
                comp, isCli, components, componentByName);
            if (wireFile is not null)
                result.Add(wireFile);
        }

        Console.Error.WriteLine($"[Posit] Wiring — generated {result.Count} Wire.cs files ({componentsWithConnections.Count} components with connections)");
        return result;
    }

    private static Component? FindCliComponent(Component[] components)
    {
        // The CLI component is the one with connections (it has a Wire.cs).
        // Prefer the one with "Program" in publicSurface or console stubs,
        // but only among components that have connections.
        var withConnections = components.Where(c => c.Connections is { Length: > 0 }).ToList();

        if (withConnections.Count == 1)
            return withConnections[0];

        if (withConnections.Count > 1)
        {
            var prog = withConnections.FirstOrDefault(c =>
                c.PublicSurface?.Contains("Program", StringComparer.OrdinalIgnoreCase) == true);
            if (prog is not null) return prog;

            var dependedUpon = new HashSet<string>(
                components.SelectMany(c => c.Dependencies ?? []),
                StringComparer.OrdinalIgnoreCase);
            var topOfChain = withConnections.FirstOrDefault(c => !dependedUpon.Contains(c.Name));
            if (topOfChain is not null) return topOfChain;

            return withConnections[0];
        }

        // Fallback: no components with connections
        var cli = components.FirstOrDefault(c =>
            c.PublicSurface?.Contains("Program") == true ||
            (c.Classification == ModuleClassification.IoShell &&
             c.StubNames?.Any(s => s.Contains("console") || s.Contains("io-console")) == true));

        cli ??= components.FirstOrDefault(c =>
            !components.Any(other => (other.Dependencies ?? []).Contains(c.Name, StringComparer.OrdinalIgnoreCase)));

        return cli;
    }

    private SourceCodeFile? GenerateComponentWiring(
        Component comp,
        bool isCli,
        Component[] allComponents,
        Dictionary<string, Component> componentByName)
    {
        if (comp.MethodSignatures is null || comp.MethodSignatures.Length == 0)
        {
            Console.Error.WriteLine($"[Posit] Wiring — REJECT: '{comp.Name}' has no methodSignatures.");
            return null;
        }
        if (comp.Connections is null || comp.Connections.Length == 0)
        {
            Console.Error.WriteLine($"[Posit] Wiring — REJECT: '{comp.Name}' has no connections.");
            return null;
        }

        Console.Error.WriteLine($"[Posit] Wiring — generating wiring for '{comp.Name}' ({comp.Connections.Length} connections, isCli={isCli})");

        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated wiring file — DETERMINISTIC from carapace connector specs.");
        sb.AppendLine("// The orchestrator read methodSignatures + connections from the architecture contract");
        sb.AppendLine("// and generated real C# calls with type conversions. No model judgment.");
        sb.AppendLine();

        EmitUsingStatements(sb, comp, allComponents);
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine();
        sb.AppendLine($"namespace {comp.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Wiring for {comp.Name} — connects to its dependencies per carapace connector specs.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class Wire");
        sb.AppendLine("    {");

        var (entryMethodName, entryParams) = ResolveEntryMethod(comp);

        if (isCli)
            EmitCliWiring(sb, comp, entryMethodName, entryParams, componentByName);
        else
            EmitNonCliWiring(sb, comp, entryMethodName, entryParams, componentByName);

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var wiringPath = $"{comp.Name}/Wire.cs";
        var content = sb.ToString();
        Console.Error.WriteLine($"[Posit] Wiring — {wiringPath}: {content.Split('\n').Length} lines");
        return new SourceCodeFile(wiringPath, content);
    }

    private static void EmitUsingStatements(StringBuilder sb, Component comp, Component[] allComponents)
    {
        var connectionTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conn in comp.Connections!)
        {
            if (!string.IsNullOrWhiteSpace(conn.ToComponent))
                connectionTargets.Add(conn.ToComponent);
        }
        connectionTargets.Add(comp.Name);

        foreach (var c in allComponents)
        {
            if (connectionTargets.Contains(c.Name))
            {
                if (c.Classification == ModuleClassification.IoShell)
                    sb.AppendLine($"using {c.Name};");
                else
                    sb.AppendLine($"using _module_{c.Name};");
            }
        }
    }

    private (string MethodName, MethodParam[] Params) ResolveEntryMethod(Component comp)
    {
        var entrySigs = comp.MethodSignatures!;
        var entrySig = entrySigs.FirstOrDefault() ?? entrySigs[0];
        var entryMethodName = entrySig.PatternMethod ?? entrySig.Name;
        var entryParams = entrySig.Params;

        // 1. Check scanned methods first — what's actually in the translated C#
        if (_scannedMethods.TryGetValue(comp.Name, out var scanned) && scanned.Count > 0)
        {
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, entryMethodName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                entryMethodName = match.Name;
                entryParams = match.ParamTypes.Select((t, i) =>
                    new MethodParam(
                        match.ParamNames.Length > i ? match.ParamNames[i] : $"arg{i}",
                        CsTypeToDafnyType(t), CsTypeToDafnyType(t))).ToArray();
                return (entryMethodName, entryParams);
            }
        }

        // 2. Fall back to pattern registry
        var patternFullSigs = GetPatternSignaturesForComponent(comp);
        if (patternFullSigs is { Count: > 0 })
        {
            var patternSig = patternFullSigs.FirstOrDefault(s =>
                string.Equals(s.Name, entryMethodName, StringComparison.OrdinalIgnoreCase))
                ?? patternFullSigs[0];
            entryParams = patternSig.Params;
            if (entrySig.PatternMethod is string pm && !string.IsNullOrWhiteSpace(pm))
                entryMethodName = pm;
            else
                entryMethodName = patternSig.Name;
        }

        return (entryMethodName, entryParams);
    }

    private void EmitCliWiring(StringBuilder sb, Component comp,
        string entryMethodName, MethodParam[] entryParams,
        Dictionary<string, Component> componentByName)
    {
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Calls {comp.Name}.{entryMethodName}() — the program's main entry point.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static int Run(string[] args)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (args.Length == 0)");
        sb.AppendLine("            {");
        sb.AppendLine($"                System.Console.WriteLine(\"Usage: {comp.Name} <input>\");");
        sb.AppendLine("                return 1;");
        sb.AppendLine("            }");
        sb.AppendLine();

        for (int i = 0; i < entryParams.Length; i++)
        {
            var param = entryParams[i];
            var dafnyType = param.DafnyType ?? param.Type;
            var paramName = EscapeReservedKeyword(param.Name);  // avoid 'args' collision with Run(string[] args)
            // io-shell CLI: entry params are C# strings (args[0] directly).
            // Dafny CLI: entry params are Dafny strings (ISequence<Rune> via UnicodeFromString).
            var callerIsIoShell = comp.Classification == ModuleClassification.IoShell;
            if (i == 0)
            {
                if (dafnyType == "string")
                {
                    if (callerIsIoShell)
                        sb.AppendLine($"            var {paramName} = args[0];");
                    else
                        sb.AppendLine($"            var {paramName} = Dafny.Sequence<Dafny.Rune>.UnicodeFromString(args[0]);");
                }
                else if (dafnyType == "int")
                    sb.AppendLine($"            var {paramName} = BigInteger.Parse(args[0]);");
                else if (dafnyType == "bool")
                    sb.AppendLine($"            var {paramName} = bool.Parse(args[0]);");
                else
                    sb.AppendLine($"            var {paramName} = default({MapDafnyTypeToCSharp(dafnyType)});");
            }
            else
            {
                sb.AppendLine($"            var {paramName} = {DefaultForDafnyType(dafnyType)}; // default for {dafnyType}");
            }
        }
        sb.AppendLine();

        var paramNames = string.Join(", ", entryParams.Select(p => EscapeReservedKeyword(p.Name)));

        // For Dafny components, call the entry method on __default.
        // For io-shell CLI components, there's no __default — skip the entry call
        // and go straight to connection calls (the CLI delegates to its logic deps).
        if (comp.Classification != ModuleClassification.IoShell)
        {
            sb.AppendLine($"            var result = _module_{comp.Name}.__default.{entryMethodName}({paramNames});");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("            // io-shell CLI — no entry call, delegate to connections");
            sb.AppendLine("            var result = 0;");  // placeholder
            sb.AppendLine();
        }

        AppendConnectionCalls(sb, comp, componentByName, entryParams);

        sb.AppendLine("            System.Console.WriteLine(result);");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
    }

    private void EmitNonCliWiring(StringBuilder sb, Component comp,
        string entryMethodName, MethodParam[] entryParams,
        Dictionary<string, Component> componentByName)
    {
        var paramNames = string.Join(", ", entryParams.Select(p => EscapeReservedKeyword(p.Name)));
        // Qualify types with the component's namespace to avoid ambiguity
        // when multiple modules define the same type (e.g. _IEntity)
        // For io-shell components, string params are C# string, not ISequence<Rune>.
        var callerIsIoShell = comp.Classification == ModuleClassification.IoShell;
        var paramDecls = string.Join(", ", entryParams.Select(p =>
        {
            var dafnyType = p.DafnyType ?? p.Type;
            // io-shell callers use C# types (string), Dafny callers use Dafny types (ISequence<Rune>)
            var csType = (callerIsIoShell && dafnyType == "string")
                ? "string"
                : QualifyType(MapDafnyTypeToCSharp(dafnyType), comp.Name);
            var name = EscapeReservedKeyword(p.Name);
            return $"{csType} {name}";
        }));

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Wires {comp.Name}'s connections to its dependencies.");
        sb.AppendLine($"        /// Chains {comp.Connections!.Length} connection calls per the carapace connector specs.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static void Wire_{comp.Name}({paramDecls})");
        sb.AppendLine("        {");

        // For Dafny components, call the entry method on __default.
        // For io-shell components, there's no __default — skip the entry call
        // and go straight to connection calls (the component delegates to its deps).
        if (comp.Classification != ModuleClassification.IoShell)
        {
            sb.AppendLine($"            var result = _module_{comp.Name}.__default.{entryMethodName}({paramNames});");
        }
        else
        {
            sb.AppendLine("            // io-shell — no __default entry call, delegate to connections");
            sb.AppendLine("            var result = 0;");
        }
        sb.AppendLine();

        AppendConnectionCalls(sb, comp, componentByName, entryParams);

        sb.AppendLine("        }");
    }

    private void AppendConnectionCalls(
        StringBuilder sb, Component comp,
        Dictionary<string, Component> componentByName,
        MethodParam[] entryParams)
    {
        sb.AppendLine("            // === Connection calls per carapace connector specs ===");

        var sourceToReturnVar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var priorReturnVarOrder = new List<(string VarName, string ReturnType, string CsReturnType, bool FromDafny)>();

        foreach (var p in entryParams)
            sourceToReturnVar[p.Name] = p.Name;

        foreach (var conn in comp.Connections!)
        {
            var toComp = componentByName.GetValueOrDefault(conn.ToComponent);
            if (toComp is null)
            {
                sb.AppendLine($"            // WARNING: connection to '{conn.ToComponent}' — component not found");
                continue;
            }

            var toMethod = ResolveToMethod(toComp, conn.ToMethod);
            // Strip generic type params from the call — C# infers them from arguments.
            // e.g. "UnwrapOr<__T>" → "UnwrapOr"
            var toMethodCallName = toMethod;
            var genIdx = toMethodCallName.IndexOf('<');
            if (genIdx > 0)
                toMethodCallName = toMethodCallName[..genIdx];

            string toClass = toComp.Classification == ModuleClassification.IoShell
                ? $"{conn.ToComponent}.{ResolveStubClass(toComp, toMethod)}"
                : $"_module_{conn.ToComponent}.__default";

            var connReturnType = conn.ReturnType ?? "var";
            var targetFullSig = ResolveTargetSignature(toComp, toMethod);

            if (targetFullSig is not null && (
                string.IsNullOrWhiteSpace(targetFullSig.ReturnType) ||
                targetFullSig.ReturnType.Equals("void", StringComparison.OrdinalIgnoreCase)))
            {
                connReturnType = "void";
            }
            else if (toComp.Classification == ModuleClassification.IoShell)
            {
                var methodLower = toMethod.ToLowerInvariant();
                if (methodLower.Contains("print") || methodLower.Contains("write")
                    || methodLower == "clear" || methodLower.Contains("log"))
                    connReturnType = "void";
            }

            var resolvedArgs = BuildFullArgList(targetFullSig, conn.ArgMappings,
                sourceToReturnVar, priorReturnVarOrder, entryParams);

            // ── Type conversion at Dafny/io-shell boundary ──
            // Dafny strings are ISequence<Rune>, io-shell strings are C# string.
            // When passing a value from one domain to the other, convert.
            var convertedArgs = new List<string>(resolvedArgs.Count);
            for (int ai = 0; ai < resolvedArgs.Count; ai++)
            {
                var arg = resolvedArgs[ai];
                if (ai < targetFullSig?.Params.Length)
                {
                    var targetParamType = targetFullSig.Params[ai].DafnyType ?? targetFullSig.Params[ai].Type;
                    var targetIsIoShell = toComp.Classification == ModuleClassification.IoShell;
                    var argConverted = ConvertDafnyIoShellBoundary(arg, targetParamType, targetIsIoShell,
                        sourceToReturnVar, priorReturnVarOrder, entryParams, comp);
                    convertedArgs.Add(argConverted);
                }
                else
                {
                    convertedArgs.Add(arg);
                }
            }
            var connArgsStr = string.Join(", ", convertedArgs);

            // Use unique variable name per connection (append index to avoid duplicates)
            var connIdx = Array.IndexOf(comp.Connections!, conn);
            var returnVarName = $"{conn.ToComponent.ToLowerInvariant()}Result_{connIdx}";

            sb.AppendLine($"            // {conn.FromMethod} → {conn.ToComponent}.{toMethodCallName}({connArgsStr})");
            sb.AppendLine($"            // {conn.ReturnUsage ?? "result stored"}");

            if (connReturnType != "void")
            {
                sb.AppendLine($"            var {returnVarName} = {toClass}.{toMethodCallName}({connArgsStr});");
                sourceToReturnVar[conn.ToComponent] = returnVarName;
                sourceToReturnVar[conn.ToComponent.ToLowerInvariant()] = returnVarName;

                // Track the raw C# return type for Dafny/io-shell boundary conversion
                var rawCsReturn = connReturnType;
                var fromDafny = toComp.Classification != ModuleClassification.IoShell;
                if (targetFullSig is not null && _scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
                {
                    var csMethod = scanned.FirstOrDefault(m => string.Equals(m.Name, toMethod, StringComparison.OrdinalIgnoreCase));
                    if (csMethod is not null)
                        rawCsReturn = csMethod.ReturnType;
                }
                priorReturnVarOrder.Add((returnVarName, connReturnType, rawCsReturn, fromDafny));

                if (!string.IsNullOrWhiteSpace(connReturnType) && connReturnType != "var")
                {
                    var simpleName = connReturnType.Split('<', '(', '.')[0].Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(simpleName) && simpleName != "void")
                        sourceToReturnVar[simpleName] = returnVarName;
                }
            }
            else
            {
                sb.AppendLine($"            {toClass}.{toMethodCallName}({connArgsStr});");
            }
            sb.AppendLine();
        }
    }

    private string ResolveToMethod(Component toComp, string connToMethod)
    {
        // 1. Check scanned methods — what's actually in the translated C#
        if (_scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
        {
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, connToMethod, StringComparison.OrdinalIgnoreCase)
                && m.GenericParams.Length == 0);  // skip generic utility methods
            if (match is not null)
                return match.Name;

            // Check PatternMethod mapping (also non-generic)
            if (toComp.MethodSignatures is { Length: > 0 })
            {
                var targetSig = toComp.MethodSignatures.FirstOrDefault(s =>
                    string.Equals(s.Name, connToMethod, StringComparison.OrdinalIgnoreCase));
                if (targetSig?.PatternMethod is string pm && !string.IsNullOrWhiteSpace(pm))
                {
                    var pmMatch = scanned.FirstOrDefault(m =>
                        string.Equals(m.Name, pm, StringComparison.OrdinalIgnoreCase)
                        && m.GenericParams.Length == 0);
                    if (pmMatch is not null) return pmMatch.Name;
                }
            }

            // Fuzzy match — non-generic only
            var fuzzy = scanned.FirstOrDefault(m =>
                m.GenericParams.Length == 0  // skip generic utility methods
                && (m.Name.Contains(connToMethod, StringComparison.OrdinalIgnoreCase)
                    || connToMethod.Contains(m.Name, StringComparison.OrdinalIgnoreCase)));
            if (fuzzy is not null) return fuzzy.Name;

            // If still no match, and there's only one non-runtime, non-generic method, use it
            var logicMethods = scanned.Where(m =>
                !m.Name.StartsWith("create_") && !m.Name.StartsWith("Default")
                && !m.Name.StartsWith("_TypeDescriptor") && m.GenericParams.Length == 0
                && m.Name != "IsSuccess" && m.Name != "IsFailure"
                && m.Name != "UnwrapOr" && m.Name != "MapResult").ToList();
            if (logicMethods.Count == 1)
                return logicMethods[0].Name;
        }

        // 2. Fall back to pattern registry (old behavior)
        var toMethod = connToMethod;
        if (toComp.MethodSignatures is { Length: > 0 })
        {
            var targetSig = toComp.MethodSignatures.FirstOrDefault(s =>
                string.Equals(s.Name, connToMethod, StringComparison.OrdinalIgnoreCase));
            if (targetSig?.PatternMethod is string pm && !string.IsNullOrWhiteSpace(pm))
                toMethod = pm;
        }

        if (toMethod == connToMethod && !string.IsNullOrWhiteSpace(toComp.PatternName))
        {
            var patternSigs = GetPatternSignaturesForComponent(toComp);
            if (patternSigs is { Count: > 0 })
            {
                var match = patternSigs.FirstOrDefault(s =>
                    string.Equals(s.Name, connToMethod, StringComparison.OrdinalIgnoreCase));
                toMethod = match is not null ? match.Name : patternSigs[0].Name;
            }
        }
        return toMethod;
    }

    private List<MethodSignature>? GetPatternSignaturesForComponent(Component comp)
    {
        if (string.IsNullOrWhiteSpace(comp.PatternName)) return null;
        try { return _registry.GetPatternSignatures(comp.PatternName); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Wiring — pattern signatures for '{comp.PatternName}': {ex.Message}");
            return null;
        }
    }

    private MethodSignature? ResolveTargetSignature(Component toComp, string toMethod)
    {
        // 1. Check scanned methods — build a MethodSignature from the real C#
        if (_scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
        {
            var csMethod = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (csMethod is not null)
            {
                // Convert C# types back to Dafny-ish types for the wiring helpers
                var dafnyParams = csMethod.ParamTypes.Select(CsTypeToDafnyType).ToArray();
                var dafnyReturn = CsTypeToDafnyType(csMethod.ReturnType);
                return new MethodSignature(
                    csMethod.Name,
                    dafnyParams.Select((t, i) =>
                        new MethodParam(csMethod.ParamNames.Length > i ? csMethod.ParamNames[i] : $"arg{i}", t, t)).ToArray(),
                    dafnyReturn,
                    dafnyReturn);
            }
        }

        // 2. Fall back to pattern registry
        var patternSigs = GetPatternSignaturesForComponent(toComp);
        if (patternSigs is { Count: > 0 })
        {
            var sig = patternSigs.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null) return sig;
        }

        // 3. Fall back to component MethodSignatures
        if (toComp.MethodSignatures is { Length: > 0 })
        {
            var sig = toComp.MethodSignatures.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.PatternMethod, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null) return sig;
        }
        return null;
    }

    /// <summary>
    /// Convert a C# type from the scanned output to a Dafny-ish type string
    /// for use with the wiring helpers (IsTypeCompatible, DefaultForDafnyType, etc.)
    /// </summary>
    private static string CsTypeToDafnyType(string csType)
    {
        var t = csType.Trim();
        if (t == "void") return "void";
        if (t == "bool") return "bool";
        if (t == "BigInteger") return "int";
        if (t == "string") return "string";  // C# string (io-shell) vs Dafny string

        // Count nesting depth of ISequence — if >1 level, it's seq<seq<X>>
        var seqCount = CountOccurrences(t, "ISequence<");
        if (seqCount >= 2)
        {
            // Nested: extract outermost inner type and recurse
            var inner = ExtractInner(t, "ISequence<", ">");
            return $"seq<{CsTypeToDafnyType(inner)}>";
        }
        if (seqCount == 1)
        {
            // Single level — check if inner is Rune (Dafny string) or something else
            var inner = ExtractInner(t, "ISequence<", ">");
            if (inner.Contains("Rune")) return "string";
            return $"seq<{CsTypeToDafnyType(inner)}>";
        }
        if (t.Contains("ISet<"))
        {
            var inner = ExtractInner(t, "ISet<", ">");
            return $"set<{CsTypeToDafnyType(inner)}>";
        }
        return t;
    }

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = source.IndexOf(pattern, idx)) >= 0) { count++; idx += pattern.Length; }
        return count;
    }

    private static string ExtractInner(string type, string open, string close)
    {
        var start = type.IndexOf(open);
        if (start < 0) return type;
        start += open.Length;
        var end = type.LastIndexOf(close);
        if (end < start) return type;
        return type[start..end].Trim();
    }

    /// <summary>
    /// Scan io-shell stub template files and add their methods to the scanned map.
    /// The stubs are in patterns/csharp-stubs/*.cs.template with {{ComponentName}} placeholders.
    /// </summary>
    private void ScanIoShellStubs(ArchitectureContract arch)
    {
        var stubsDir = Path.Combine(_registry.PatternsDirectory, "csharp-stubs");
        if (!Directory.Exists(stubsDir)) return;

        foreach (var comp in arch.Components)
        {
            if (comp.Classification != ModuleClassification.IoShell) continue;
            if (comp.StubNames is null || comp.StubNames.Length == 0) continue;

            foreach (var stubName in comp.StubNames)
            {
                var templatePath = Path.Combine(stubsDir, $"{stubName}.cs.template");
                if (!File.Exists(templatePath)) continue;

                var content = File.ReadAllText(templatePath)
                    .Replace("{{ComponentName}}", comp.Name)
                    .Replace("{{componentName}}", comp.Name);
                var methods = _scanner.ScanContent(content);
                _scannedMethods[comp.Name] = methods;
                Console.Error.WriteLine($"[Posit] Scanner — io-shell '{comp.Name}' ({stubName}): {methods.Count} methods");
            }
        }
    }

    private static List<string> BuildFullArgList(
        MethodSignature? targetSig, string[]? argMappings,
        Dictionary<string, string> sourceToReturnVar,
        List<(string VarName, string ReturnType, string CsReturnType, bool FromDafny)> priorReturnVarOrder,
        MethodParam[] entryParams)
    {
        if (targetSig is null || targetSig.Params.Length == 0)
        {
            var fallback = new List<string>();
            if (argMappings?.Length > 0)
            {
                foreach (var am in argMappings)
                {
                    var arrow = am.IndexOf("->");
                    string src = arrow > 0 ? am[..arrow].Trim() : am.Trim();
                    fallback.Add(sourceToReturnVar.TryGetValue(src, out var v) ? v : $"/* unresolved: {src} */ null");
                }
            }
            return fallback;
        }

        var fullParams = targetSig.Params;
        var result = new List<string>(fullParams.Length);

        for (int i = 0; i < fullParams.Length; i++)
        {
            var param = fullParams[i];
            string? resolved = null;

            if (argMappings is { Length: > 0 } && i < argMappings.Length)
            {
                var am = argMappings[i];
                var arrow = am.IndexOf("->");
                string src = arrow > 0 ? am[..arrow].Trim() : am.Trim();
                if (sourceToReturnVar.TryGetValue(src, out var v))
                {
                    var pType = param.DafnyType ?? param.Type;
                    var sInfo = priorReturnVarOrder.FirstOrDefault(x => x.VarName == v);
                    var sType = sInfo != default ? sInfo.ReturnType : "string";
                    if (IsTypeCompatible(sType, pType)) resolved = v;
                }
            }

            if (resolved is null && sourceToReturnVar.TryGetValue(param.Name, out var nm))
            {
                var pType = param.DafnyType ?? param.Type;
                var sInfo = priorReturnVarOrder.FirstOrDefault(x => x.VarName == nm);
                var sType = sInfo != default ? sInfo.ReturnType : "string";
                if (IsTypeCompatible(sType, pType)) resolved = nm;
            }

            if (resolved is null)
            {
                var pType = param.DafnyType ?? param.Type;
                var priors = priorReturnVarOrder
                    .Where(v => !entryParams.Any(p => p.Name == v.VarName))
                    .Distinct().ToList();
                var match = priors.FirstOrDefault(v => IsTypeCompatible(v.ReturnType, pType));
                if (match != default) resolved = match.VarName;
            }

            resolved ??= DefaultForDafnyType(param.DafnyType ?? param.Type);
            result.Add(resolved);
        }
        return result;
    }

    // === Type helpers ===

    /// <summary>
    /// Convert a value at the Dafny/io-shell boundary.
    /// Dafny strings are ISequence&lt;Rune&gt;; io-shell strings are C# string.
    /// When a value crosses the boundary, we need to convert:
    ///   - ISequence&lt;Rune&gt; → string: Dafny.Helpers.SequenceToString(seq)
    ///   - string → ISequence&lt;Rune&gt;: Dafny.Sequence&lt;Dafny.Rune&gt;.UnicodeFromString(s)
    ///
    /// We detect the source type by checking whether the variable came from a
    /// Dafny-translated method (ISequence&lt;Rune&gt;) or an io-shell/entry param (string).
    /// </summary>
    private static string ConvertDafnyIoShellBoundary(
        string arg,
        string targetParamType,
        bool targetIsIoShell,
        Dictionary<string, string> sourceToReturnVar,
        List<(string VarName, string ReturnType, string CsReturnType, bool FromDafny)> priorReturnVarOrder,
        MethodParam[] entryParams,
        Component callerComp)
    {
        if (string.IsNullOrEmpty(arg) || arg.StartsWith("/*") || arg == "null!")
            return arg;

        // Only convert string-type params
        var targetIsString = targetParamType == "string";
        if (!targetIsString) return arg;

        // Dafny string defaults (UnicodeFromString) → C# string "" when target is io-shell
        if (targetIsIoShell && arg.Contains("UnicodeFromString"))
            return "\"\"";

        // C# string literals → Dafny ISequence<Rune> when target is Dafny
        if (!targetIsIoShell && arg == "\"\"")
            return "Dafny.Sequence<Dafny.Rune>.UnicodeFromString(\"\")";

        // Skip other literals and defaults — they're already in the right form
        if (arg.StartsWith("Dafny.") || arg.StartsWith("BigInteger") || arg.StartsWith("bool.")
            || arg.StartsWith("default(") || arg == "false" || arg == "true")
            return arg;

        // 1. Entry params: check the caller's domain
        var isEntryParam = entryParams.Any(p => EscapeReservedKeyword(p.Name) == arg || p.Name == arg);
        if (isEntryParam)
        {
            var callerIsDafny = callerComp.Classification != ModuleClassification.IoShell;
            if (callerIsDafny && targetIsIoShell)
            {
                // Entry param is ISequence<Rune> (Dafny), target wants C# string
                return $"Dafny.Helpers.SequenceToString({arg})";
            }
            if (!callerIsDafny && !targetIsIoShell)
            {
                // Entry param is string (io-shell), target wants ISequence<Rune> (Dafny)
                return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({arg})";
            }
            return arg;
        }

        // 2. Prior return vars: use the tracked CsReturnType and FromDafny flag
        var priorMatch = priorReturnVarOrder.FirstOrDefault(v => v.VarName == arg);
        if (priorMatch != default)
        {
            var sourceFromDafny = priorMatch.FromDafny;
            var sourceCsReturn = priorMatch.CsReturnType;

            // Source is Dafny (returns ISequence<Rune>), target is io-shell (wants string)
            if (sourceFromDafny && targetIsIoShell
                && (sourceCsReturn.Contains("ISequence") || sourceCsReturn.Contains("Rune")))
            {
                return $"Dafny.Helpers.SequenceToString({arg})";
            }

            // Source is io-shell (returns string), target is Dafny (wants ISequence<Rune>)
            if (!sourceFromDafny && !targetIsIoShell
                && (sourceCsReturn == "string" || sourceCsReturn.Contains("string")))
            {
                return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({arg})";
            }
        }

        return arg;
    }

    /// <summary>
    /// Prefix C# reserved keywords with @ to use them as identifiers.
    /// e.g. "event" → "@event", "result" → "@result"
    /// </summary>
    private static string EscapeReservedKeyword(string name)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        { "event", "object", "string", "int", "bool", "class",
          "static", "void", "return", "new", "var", "if", "else", "for",
          "while", "switch", "case", "break", "continue", "default", "null",
          "true", "false", "this", "base", "out", "ref", "in", "params",
          "using", "namespace", "public", "private", "protected", "internal",
          "abstract", "virtual", "override", "sealed", "readonly", "const",
          "async", "await", "yield", "lock", "try", "catch", "finally",
          "throw", "typeof", "sizeof", "is", "as", "delegate", "enum",
          "struct", "interface", "get", "set", "value", "operator", "explicit",
          "implicit", "partial", "where", "select", "from", "group", "into",
          "orderby", "join", "let", "on", "equals", "by", "ascending",
          "descending", "global", "stackalloc", "fixed", "unchecked", "checked",
          "unsafe",
          // Not C# keywords but collide with CLI wiring's Run(string[] args)
          "args" };

        if (reserved.Contains(name))
            return $"@{name}";
        return name;
    }

    /// <summary>
    /// Qualify a C# type with the component's namespace if it's a Dafny interface
    /// type (starts with _) that could be ambiguous across modules.
    /// e.g. _IEntity → _module_CalcEngine._IEntity
    /// </summary>
    private static string QualifyType(string csType, string componentName)
    {
        // Only qualify bare interface types (start with _ and no namespace)
        if (csType.StartsWith("_") && !csType.Contains("."))
        {
            // Check if it's inside a generic (e.g. Dafny.ISequence<_IEntity>)
            if (csType.Contains("<"))
                return QualifyGenericType(csType, componentName);
            return $"_module_{componentName}.{csType}";
        }
        // Handle types containing unqualified Dafny interfaces in generics
        if (csType.Contains("<_") && !csType.Contains("."))
            return QualifyGenericType(csType, componentName);
        return csType;
    }

    private static string QualifyGenericType(string csType, string componentName)
    {
        // Replace bare _TypeName with _module_Component._TypeName inside generics
        // e.g. Dafny.ISequence<_IEntity> → Dafny.ISequence<_module_CalcEngine._IEntity>
        return System.Text.RegularExpressions.Regex.Replace(csType,
            @"(?<![\w.])(_\w+)",
            m => $"_module_{componentName}.{m.Value}");
    }

    public static bool IsValidationType(string returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType)) return false;
        var l = returnType.ToLowerInvariant().Trim();
        return l.Contains("validation") || l.Contains("result") || l == "bool"
            || l.Contains("success") || l.Contains("failure");
    }

    public static bool IsTypeCompatible(string returnType, string paramType)
    {
        if (string.IsNullOrWhiteSpace(returnType) || string.IsNullOrWhiteSpace(paramType)) return false;
        var r = returnType.ToLowerInvariant().Trim();
        var p = paramType.ToLowerInvariant().Trim();
        if (r == p) return true;
        if (r.StartsWith("seq<") && r.EndsWith('>') && p.StartsWith("seq<") && p.EndsWith('>'))
            return IsTypeCompatible(r[4..^1].Trim(), p[4..^1].Trim());
        if (r.StartsWith("set<") && r.EndsWith('>') && p.StartsWith("set<") && p.EndsWith('>'))
            return IsTypeCompatible(r[4..^1].Trim(), p[4..^1].Trim());
        if (r == "string" && p == "string") return true;
        if ((r == "int" || r == "bigint") && (p == "int" || p == "bigint")) return true;
        if (r == "var") return true;
        return false;
    }

    public static string DefaultForDafnyType(string dafnyType)
    {
        var t = dafnyType.Trim();
        // Handle generic type params (T, __T, __U) — can't emit default(T) in wiring
        // because T isn't declared. Use null! cast to the return type.
        if (t.Length <= 3 && (t == "T" || t.StartsWith("__") || t.StartsWith("T_")))
            return "null!";
        if (t.Contains("<T>") || t.Contains("<__T>") || t.Contains("<__U>"))
            return "null!";
        if (t.StartsWith("seq<", StringComparison.Ordinal) && t.EndsWith('>'))
            return $"Dafny.Sequence<{MapDafnyTypeToCSharp(t[4..^1].Trim())}>.Empty";
        if (t.StartsWith("set<", StringComparison.Ordinal) && t.EndsWith('>'))
            return $"Dafny.Set<{MapDafnyTypeToCSharp(t[4..^1].Trim())}>.Empty";
        return t switch
        {
            "int" => "BigInteger.Zero",
            "bool" => "false",
            "string" => "Dafny.Sequence<Dafny.Rune>.UnicodeFromString(\"\")",
            _ => $"default({MapDafnyTypeToCSharp(t)})"
        };
    }

    public static string MapDafnyTypeToCSharp(string dafnyType)
    {
        var t = dafnyType.Trim();
        return t switch
        {
            "int" => "BigInteger",
            "bool" => "bool",
            "string" => "Dafny.ISequence<Dafny.Rune>",
            _ when t.StartsWith("seq<") => "Dafny.ISequence<" + MapDafnyTypeToCSharp(t[4..^1]) + ">",
            _ when t.StartsWith("set<") => "Dafny.ISet<" + MapDafnyTypeToCSharp(t[4..^1]) + ">",
            _ => t
        };
    }

    private static string ResolveStubClass(Component targetComp, string methodName)
    {
        var m = methodName.ToLowerInvariant();
        if (m.Contains("file") || m.Contains("read") && !m.Contains("console")) return "FileIO";
        if (m.Contains("print") || m.Contains("console") || m.Contains("readline")) return "ConsoleIO";
        if (m.Contains("stream") || m.Contains("chunk")) return "StreamIO";
        if (m is "get" or "post" or "put" or "delete" or "http") return "NetworkIO";
        if (m.Contains("query") || m.Contains("execute") || m.Contains("connection")) return "DatabaseIO";
        if (m.Contains("time") || m.Contains("sleep") || m.Contains("random")) return "TimeRandom";
        if (targetComp.StubNames?.Length > 0) return StubNameToClassName(targetComp.StubNames[0]);
        return "FileIO";
    }

    private static string StubNameToClassName(string stubName)
    {
        var knownAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "io", "ci", "cd" };
        var parts = stubName.Split('-');
        return string.Concat(parts.Select(p =>
        {
            if (p.Length == 0) return "";
            if (knownAbbreviations.Contains(p)) return p.ToUpperInvariant();
            return char.ToUpperInvariant(p[0]) + p[1..];
        }));
    }
}