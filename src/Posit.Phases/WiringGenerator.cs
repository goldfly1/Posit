using System.Text;
using System.Text.RegularExpressions;
using Posit.Contracts.Artifacts;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Generates Wire.cs files — one per component with connections.
/// Uses TranslatedCSharpScanner to read the actual translated C# and wire
/// against real method signatures — no guessing from pattern files.
///
/// TYPE TRACKING: every variable is tracked with its actual C# type (from the
/// scanner). When a call crosses the Dafny/io-shell boundary (ISequence of Rune
/// vs string), the conversion is emitted inline. No Dafny-type-space mapping,
/// no boundary detection pass, no patches.
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

    // ── A tracked variable: name + actual C# type ──
    private record VarInfo(string Name, string CsType);

    // ── C# type aliases for Dafny types ──
    private const string DafnyString = "Dafny.ISequence<Dafny.Rune>";
    private const string CsString = "string";

    public List<SourceCodeFile> Generate(
        ArchitectureContract arch,
        List<(string ModuleName, string CSharpPath)> translatedFiles)
    {
        _scannedMethods = _scanner.ScanAll(translatedFiles);
        ScanIoShellStubs(arch);
        var result = new List<SourceCodeFile>();
        var components = arch.Components;
        if (components.Length == 0) return result;

        var componentByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
            componentByName[c.Name] = c;

        var cliComponent = FindCliComponent(components);
        if (cliComponent is null) return result;

        var componentsWithConnections = components
            .Where(c => c.Connections?.Length > 0 && c.MethodSignatures?.Length > 0)
            .ToList();

        foreach (var comp in componentsWithConnections)
        {
            var isCli = string.Equals(comp.Name, cliComponent.Name, StringComparison.OrdinalIgnoreCase);
            var wireFile = GenerateComponentWiring(comp, isCli, components, componentByName);
            if (wireFile is not null)
                result.Add(wireFile);
        }

        Console.Error.WriteLine($"[Posit] Wiring — generated {result.Count} Wire.cs files ({componentsWithConnections.Count} components with connections)");
        return result;
    }

    private static Component? FindCliComponent(Component[] components)
    {
        var withConnections = components.Where(c => c.Connections is { Length: > 0 }).ToList();
        if (withConnections.Count == 1) return withConnections[0];
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
        var cli = components.FirstOrDefault(c =>
            c.PublicSurface?.Contains("Program") == true ||
            (c.Classification == ModuleClassification.IoShell &&
             c.StubNames?.Any(s => s.Contains("console") || s.Contains("io-console")) == true));
        cli ??= components.FirstOrDefault(c =>
            !components.Any(other => (other.Dependencies ?? []).Contains(c.Name, StringComparer.OrdinalIgnoreCase)));
        return cli;
    }

    private SourceCodeFile? GenerateComponentWiring(
        Component comp, bool isCli,
        Component[] allComponents,
        Dictionary<string, Component> componentByName)
    {
        if (comp.MethodSignatures is null || comp.MethodSignatures.Length == 0) return null;
        if (comp.Connections is null || comp.Connections.Length == 0) return null;

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

        if (isCli)
            EmitCliWiring(sb, comp, componentByName);
        else
            EmitNonCliWiring(sb, comp, componentByName);

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
            if (!string.IsNullOrWhiteSpace(conn.ToComponent))
                connectionTargets.Add(conn.ToComponent);
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

    // ════════════════════════════════════════════════════════════════════════
    // CLI WIRING — Run(string[] args) entry point
    // ════════════════════════════════════════════════════════════════════════

    private void EmitCliWiring(StringBuilder sb, Component comp,
        Dictionary<string, Component> componentByName)
    {
        var (entryMethod, entryVars) = ResolveEntry(comp);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Calls {comp.Name}.{entryMethod}() — the program's main entry point.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static int Run(string[] args)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (args.Length == 0)");
        sb.AppendLine("            {");
        sb.AppendLine($"                System.Console.WriteLine(\"Usage: {comp.Name} <input>\");");
        sb.AppendLine("                return 1;");
        sb.AppendLine("            }");
        sb.AppendLine();

        // Emit entry param variables with correct C# types
        var callerIsIoShell = comp.Classification == ModuleClassification.IoShell;
        for (int i = 0; i < entryVars.Count; i++)
        {
            var v = entryVars[i];
            var safeName = SafeName(v.Name);
            if (i == 0)
            {
                if (v.CsType == DafnyString)
                    sb.AppendLine($"            var {safeName} = Dafny.Sequence<Dafny.Rune>.UnicodeFromString(args[0]);");
                else if (v.CsType == CsString)
                    sb.AppendLine($"            var {safeName} = args[0];");
                else if (v.CsType.Contains("BigInteger"))
                    sb.AppendLine($"            var {safeName} = BigInteger.Parse(args[0]);");
                else if (v.CsType == "bool")
                    sb.AppendLine($"            var {safeName} = bool.Parse(args[0]);");
                else
                    sb.AppendLine($"            var {safeName} = {DefaultForCsType(v.CsType)};");
            }
            else
            {
                sb.AppendLine($"            var {safeName} = {DefaultForCsType(v.CsType)};");
            }
            entryVars[i] = v with { Name = safeName };
        }
        sb.AppendLine();

        // Entry call (Dafny only — io-shell has no __default)
        if (comp.Classification != ModuleClassification.IoShell)
        {
            var argList = string.Join(", ", entryVars.Select(v => v.Name));
            sb.AppendLine($"            var result = _module_{comp.Name}.__default.{entryMethod}({argList});");
        }
        else
        {
            sb.AppendLine("            // io-shell CLI — no __default, delegate to connections");
            sb.AppendLine("            var result = 0;");
        }
        sb.AppendLine();

        AppendConnectionCalls(sb, comp, componentByName, entryVars);
        sb.AppendLine("            System.Console.WriteLine(result);");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
    }

    // ════════════════════════════════════════════════════════════════════════
    // NON-CLI WIRING — Wire_{ComponentName}(params) method
    // ════════════════════════════════════════════════════════════════════════

    private void EmitNonCliWiring(StringBuilder sb, Component comp,
        Dictionary<string, Component> componentByName)
    {
        var (entryMethod, entryVars) = ResolveEntry(comp);
        var callerIsIoShell = comp.Classification == ModuleClassification.IoShell;

        // Param declarations with correct C# types
        var paramDecls = string.Join(", ", entryVars.Select(v =>
        {
            var csType = (callerIsIoShell && v.CsType == DafnyString) ? CsString : QualifyType(v.CsType, comp.Name);
            return $"{csType} {SafeName(v.Name)}";
        }));

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Wires {comp.Name}'s connections to its dependencies.");
        sb.AppendLine($"        /// Chains {comp.Connections!.Length} connection calls per the carapace connector specs.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static void Wire_{comp.Name}({paramDecls})");
        sb.AppendLine("        {");

        // Fix var names after declaring them
        for (int i = 0; i < entryVars.Count; i++)
            entryVars[i] = entryVars[i] with { Name = SafeName(entryVars[i].Name) };

        if (comp.Classification != ModuleClassification.IoShell)
        {
            var argList = string.Join(", ", entryVars.Select(v => v.Name));
            sb.AppendLine($"            var result = _module_{comp.Name}.__default.{entryMethod}({argList});");
        }
        else
        {
            sb.AppendLine("            // io-shell — no __default, delegate to connections");
            sb.AppendLine("            var result = 0;");
        }
        sb.AppendLine();

        AppendConnectionCalls(sb, comp, componentByName, entryVars);
        sb.AppendLine("        }");
    }

    // ════════════════════════════════════════════════════════════════════════
    // CONNECTION CALLS — the core wiring logic with C# type tracking
    // ════════════════════════════════════════════════════════════════════════

    private void AppendConnectionCalls(
        StringBuilder sb, Component comp,
        Dictionary<string, Component> componentByName,
        List<VarInfo> entryVars)
    {
        sb.AppendLine("            // === Connection calls per carapace connector specs ===");

        // Variable registry: maps source names → VarInfo (name + actual C# type)
        var vars = new Dictionary<string, VarInfo>(StringComparer.OrdinalIgnoreCase);
        var varOrder = new List<VarInfo>();  // ordered list for positional fallback

        foreach (var v in entryVars)
        {
            vars[v.Name] = v;
            varOrder.Add(v);
        }

        foreach (var conn in comp.Connections!)
        {
            var toComp = componentByName.GetValueOrDefault(conn.ToComponent);
            if (toComp is null)
            {
                sb.AppendLine($"            // WARNING: connection to '{conn.ToComponent}' — component not found");
                continue;
            }

            var toMethod = ResolveToMethod(toComp, conn.ToMethod);
            var toMethodCallName = toMethod;
            var genIdx = toMethodCallName.IndexOf('<');
            if (genIdx > 0) toMethodCallName = toMethodCallName[..genIdx];

            string toClass = toComp.Classification == ModuleClassification.IoShell
                ? $"{conn.ToComponent}.{ResolveStubClass(toComp, toMethod)}"
                : $"_module_{conn.ToComponent}.__default";

            // Get target method's actual C# param types from scanner
            var targetParams = GetTargetCsParams(toComp, toMethod);
            var targetReturnType = GetTargetCsReturn(toComp, toMethod);

            // Determine if void return
            var isVoid = targetReturnType == "void" ||
                         (toComp.Classification == ModuleClassification.IoShell &&
                          (toMethod.ToLowerInvariant().Contains("print") ||
                           toMethod.ToLowerInvariant().Contains("write") ||
                           toMethod.ToLowerInvariant() == "clear" ||
                           toMethod.ToLowerInvariant().Contains("log")));

            // Build args with type conversion
            var args = BuildArgs(targetParams, conn.ArgMappings, vars, varOrder, entryVars);

            var connIdx = Array.IndexOf(comp.Connections!, conn);
            var returnVarName = $"{conn.ToComponent.ToLowerInvariant()}Result_{connIdx}";

            sb.AppendLine($"            // {conn.FromMethod} → {conn.ToComponent}.{toMethodCallName}({string.Join(", ", args)})");
            sb.AppendLine($"            // {conn.ReturnUsage ?? "result stored"}");

            if (!isVoid)
            {
                sb.AppendLine($"            var {returnVarName} = {toClass}.{toMethodCallName}({string.Join(", ", args)});");

                // Track the return var with its actual C# type
                var returnVar = new VarInfo(returnVarName, targetReturnType);
                vars[conn.ToComponent] = returnVar;
                vars[conn.ToComponent.ToLowerInvariant()] = returnVar;
                varOrder.Add(returnVar);
            }
            else
            {
                sb.AppendLine($"            {toClass}.{toMethodCallName}({string.Join(", ", args)});");
            }
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Build the argument list for a connection call.
    /// For each target param: resolve the source variable, convert types if needed.
    /// </summary>
    private List<string> BuildArgs(
        List<(string Name, string CsType)> targetParams,
        string[]? argMappings,
        Dictionary<string, VarInfo> vars,
        List<VarInfo> varOrder,
        List<VarInfo> entryVars)
    {
        var result = new List<string>(targetParams.Count);

        for (int i = 0; i < targetParams.Count; i++)
        {
            var (paramName, paramCsType) = targetParams[i];
            string? resolved = null;

            // 1. Try arg mapping (positional)
            if (argMappings is { Length: > 0 } && i < argMappings.Length)
            {
                var am = argMappings[i];
                var arrow = am.IndexOf("->");
                string src = arrow > 0 ? am[..arrow].Trim() : am.Trim();
                if (vars.TryGetValue(src, out var v))
                    resolved = ConvertType(v, paramCsType);
            }

            // 2. Try param-name match
            if (resolved is null && vars.TryGetValue(paramName, out var v2))
                resolved = ConvertType(v2, paramCsType);

            // 3. Positional fallback — most recent non-entry var with compatible type
            if (resolved is null)
            {
                var candidates = varOrder
                    .Where(v => !entryVars.Any(e => e.Name == v.Name))
                    .Where(v => CanConvert(v.CsType, paramCsType))
                    .ToList();
                if (candidates.Count > 0)
                    resolved = ConvertType(candidates[^1], paramCsType);
            }

            // 4. Default
            resolved ??= DefaultForCsType(paramCsType);
            result.Add(resolved);
        }

        return result;
    }

    /// <summary>
    /// Convert a variable from its C# type to the target C# type.
    /// Only handles the Dafny/io-shell string boundary:
    ///   ISequence<Rune> → string: Dafny.Helpers.SequenceToString(var)
    ///   string → ISequence<Rune>: Dafny.Sequence<Dafny.Rune>.UnicodeFromString(var)
    /// </summary>
    private static string ConvertType(VarInfo source, string targetCsType)
    {
        var src = source.CsType;
        var tgt = targetCsType;

        // Same type — pass directly
        if (TypesMatch(src, tgt))
            return source.Name;

        // Dafny string → C# string
        if (IsDafnyString(src) && tgt == CsString)
            return $"Dafny.Helpers.SequenceToString({source.Name})";

        // C# string → Dafny string
        if (src == CsString && IsDafnyString(tgt))
            return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({source.Name})";

        // Dafny default (UnicodeFromString("")) → C# string
        if (tgt == CsString && source.Name.Contains("UnicodeFromString"))
            return "\"\"";

        // Can't convert — pass as-is (will fail at compile, which is correct)
        return source.Name;
    }

    /// <summary>
    /// Can a value of sourceType be converted to targetType?
    /// </summary>
    private static bool CanConvert(string sourceType, string targetType)
    {
        if (TypesMatch(sourceType, targetType)) return true;
        if (IsDafnyString(sourceType) && targetType == CsString) return true;
        if (sourceType == CsString && IsDafnyString(targetType)) return true;
        return false;
    }

    private static bool TypesMatch(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        // BigInteger and int are interchangeable in Dafny C#
        if ((a.Contains("BigInteger") || a == "int") && (b.Contains("BigInteger") || b == "int")) return true;
        // var matches anything
        if (a == "var" || b == "var") return true;
        return false;
    }

    private static bool IsDafnyString(string csType)
        => csType.Contains("ISequence") && csType.Contains("Rune");

    // ════════════════════════════════════════════════════════════════════════
    // ENTRY METHOD RESOLUTION — returns method name + C# typed params
    // ════════════════════════════════════════════════════════════════════════

    private (string MethodName, List<VarInfo> Params) ResolveEntry(Component comp)
    {
        var entrySig = comp.MethodSignatures!.FirstOrDefault() ?? comp.MethodSignatures![0];
        var entryMethodName = entrySig.PatternMethod ?? entrySig.Name;

        // 1. Check scanned methods — actual C# signatures
        if (_scannedMethods.TryGetValue(comp.Name, out var scanned) && scanned.Count > 0)
        {
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, entryMethodName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                var vars = match.ParamTypes.Select((t, i) =>
                    new VarInfo(match.ParamNames.Length > i ? match.ParamNames[i] : $"arg{i}", t)).ToList();
                return (match.Name, vars);
            }
        }

        // 2. Fall back to pattern registry
        var patternSigs = GetPatternSignaturesForComponent(comp);
        if (patternSigs is { Count: > 0 })
        {
            var patternSig = patternSigs.FirstOrDefault(s =>
                string.Equals(s.Name, entryMethodName, StringComparison.OrdinalIgnoreCase))
                ?? patternSigs[0];
            // Pattern sigs use Dafny types — convert to C# types
            var vars = patternSig.Params.Select(p =>
                new VarInfo(p.Name, DafnyTypeToCsType(p.DafnyType ?? p.Type))).ToList();
            if (entrySig.PatternMethod is string pm && !string.IsNullOrWhiteSpace(pm))
                return (pm, vars);
            return (patternSig.Name, vars);
        }

        // 3. Fall back to component's MethodSignatures
        var vars3 = entrySig.Params.Select(p =>
            new VarInfo(p.Name, DafnyTypeToCsType(p.DafnyType ?? p.Type))).ToList();
        return (entryMethodName, vars3);
    }

    /// <summary>
    /// Map a Dafny type string to its C# equivalent.
    /// </summary>
    private static string DafnyTypeToCsType(string dafnyType)
    {
        var t = dafnyType.Trim();
        return t switch
        {
            "int" => "BigInteger",
            "bool" => "bool",
            "string" => DafnyString,  // Dafny string → ISequence<Rune> in C#
            _ when t.StartsWith("seq<") => $"Dafny.ISequence<{DafnyTypeToCsType(t[4..^1].Trim())}>",
            _ when t.StartsWith("set<") => $"Dafny.ISet<{DafnyTypeToCsType(t[4..^1].Trim())}>",
            _ => t
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // TARGET METHOD RESOLUTION — get actual C# param/return types from scanner
    // ════════════════════════════════════════════════════════════════════════

    private List<(string Name, string CsType)> GetTargetCsParams(Component toComp, string toMethod)
    {
        if (_scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
        {
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.ParamTypes.Select((t, i) =>
                    (match.ParamNames.Length > i ? match.ParamNames[i] : $"arg{i}", t)).ToList();
        }

        // Fall back to pattern registry
        var patternSigs = GetPatternSignaturesForComponent(toComp);
        if (patternSigs is { Count: > 0 })
        {
            var sig = patternSigs.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase))
                ?? patternSigs[0];
            return sig.Params.Select(p => (p.Name, DafnyTypeToCsType(p.DafnyType ?? p.Type))).ToList();
        }

        // Fall back to component MethodSignatures
        if (toComp.MethodSignatures is { Length: > 0 })
        {
            var sig = toComp.MethodSignatures.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.PatternMethod, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null)
                return sig.Params.Select(p => (p.Name, DafnyTypeToCsType(p.DafnyType ?? p.Type))).ToList();
        }

        return new List<(string, string)>();
    }

    private string GetTargetCsReturn(Component toComp, string toMethod)
    {
        if (_scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
        {
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.ReturnType;
        }

        var patternSigs = GetPatternSignaturesForComponent(toComp);
        if (patternSigs is { Count: > 0 })
        {
            var sig = patternSigs.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null)
                return DafnyTypeToCsType(sig.ReturnType);
        }

        return "var";
    }

    // ════════════════════════════════════════════════════════════════════════
    // METHOD NAME RESOLUTION — find the real method name from scanner
    // ════════════════════════════════════════════════════════════════════════

    private string ResolveToMethod(Component toComp, string connToMethod)
    {
        if (_scannedMethods.TryGetValue(toComp.Name, out var scanned) && scanned.Count > 0)
        {
            // Exact match (non-generic)
            var match = scanned.FirstOrDefault(m =>
                string.Equals(m.Name, connToMethod, StringComparison.OrdinalIgnoreCase)
                && m.GenericParams.Length == 0);
            if (match is not null) return match.Name;

            // PatternMethod mapping
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

            // Fuzzy match
            var fuzzy = scanned.FirstOrDefault(m =>
                m.GenericParams.Length == 0
                && (m.Name.Contains(connToMethod, StringComparison.OrdinalIgnoreCase)
                    || connToMethod.Contains(m.Name, StringComparison.OrdinalIgnoreCase)));
            if (fuzzy is not null) return fuzzy.Name;

            // Single logic method fallback
            var logicMethods = scanned.Where(m =>
                !m.Name.StartsWith("create_") && !m.Name.StartsWith("Default")
                && !m.Name.StartsWith("_TypeDescriptor") && m.GenericParams.Length == 0
                && m.Name != "IsSuccess" && m.Name != "IsFailure"
                && m.Name != "UnwrapOr" && m.Name != "MapResult").ToList();
            if (logicMethods.Count == 1) return logicMethods[0].Name;
        }

        // Fall back to pattern registry
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

    // ════════════════════════════════════════════════════════════════════════
    // IO-SHELL STUB SCANNING
    // ════════════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Safe variable name — rename "args" to avoid collision with Run(string[] args).
    /// @args doesn't work because @ is just a prefix, the name is still "args".
    /// </summary>
    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "arg0";
        if (name == "args") return "inputArgs";
        // Prefix C# reserved keywords
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        { "event", "object", "string", "int", "bool", "class", "static", "void",
          "return", "new", "var", "if", "else", "for", "while", "switch", "case",
          "break", "continue", "default", "null", "true", "false", "this", "base",
          "out", "ref", "in", "params", "using", "namespace", "public", "private",
          "protected", "internal", "abstract", "virtual", "override", "sealed",
          "readonly", "const", "async", "await", "yield", "lock", "try", "catch",
          "finally", "throw", "typeof", "sizeof", "is", "as", "delegate", "enum",
          "struct", "interface", "get", "set", "value", "operator", "explicit",
          "implicit", "partial", "where", "select", "from", "group", "into",
          "orderby", "join", "let", "on", "equals", "by", "ascending", "descending",
          "global", "stackalloc", "fixed", "unchecked", "checked", "unsafe" };
        if (reserved.Contains(name)) return $"@{name}";
        return name;
    }

    /// <summary>
    /// Default value for a C# type.
    /// </summary>
    private static string DefaultForCsType(string csType)
    {
        var t = csType.Trim();
        if (t == "void") return "";
        if (t.Contains("BigInteger")) return "BigInteger.Zero";
        if (t == "bool") return "false";
        if (t == CsString) return "\"\"";
        if (t == DafnyString) return "Dafny.Sequence<Dafny.Rune>.UnicodeFromString(\"\")";
        if (t.Contains("ISequence")) return $"Dafny.Sequence<{ExtractInner(t, "ISequence<", ">")}>.Empty";
        if (t.Contains("ISet")) return $"Dafny.Set<{ExtractInner(t, "ISet<", ">")}>.Empty";
        if (t.Length <= 3 && (t == "T" || t.StartsWith("__") || t.StartsWith("T_"))) return "null!";
        if (t.Contains("<T>") || t.Contains("<__T>")) return "null!";
        return $"default({t})";
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
    /// Qualify a C# type with the component's namespace if it's a Dafny interface
    /// type (starts with _) that could be ambiguous across modules.
    /// </summary>
    private static string QualifyType(string csType, string componentName)
    {
        if (csType.StartsWith("_") && !csType.Contains("."))
        {
            if (csType.Contains("<"))
                return QualifyGenericType(csType, componentName);
            return $"_module_{componentName}.{csType}";
        }
        if (csType.Contains("<_") && !csType.Contains("."))
            return QualifyGenericType(csType, componentName);
        return csType;
    }

    private static string QualifyGenericType(string csType, string componentName)
    {
        return Regex.Replace(csType, @"(?<![\w.])(_\w+)", m => $"_module_{componentName}.{m.Value}");
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