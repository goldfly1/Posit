namespace Posit.Phases;

/// <summary>Variable info for wiring — name and ACTUAL C# type.</summary>
public record VarInfo(string Name, string CsType);

/// <summary>
/// Generates Wire.cs files. Tracks ACTUAL C# types (from TranslatedCSharpScanner),
/// converts types at Dafny/io-shell boundaries, resolves actual class names
/// (Frame vs __default from scanner, not hardcoded).
/// </summary>
public static class WiringGenerator
{
    /// <summary>
    /// Generate Wire.cs for a component with connections.
    /// </summary>
    public static string Generate(
        Component comp,
        ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translatedSignatures,
        Dictionary<string, List<CsMethodSignature>> stubSignatures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// {comp.Name}/Wire.cs — auto-generated wiring");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Dafny;");
        sb.AppendLine();

        var isCli = comp.Connections.Length > 0;
        var className = ResolveDafnyClass(comp, translatedSignatures);

        sb.AppendLine($"namespace {comp.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    public static class Wire");
        sb.AppendLine("    {");

        if (isCli)
            EmitCliWiring(sb, comp, contract, translatedSignatures, stubSignatures, className);
        else
            EmitNonCliWiring(sb, comp, contract, translatedSignatures, stubSignatures);

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitCliWiring(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translated, Dictionary<string, List<CsMethodSignature>> stubs,
        string className)
    {
        sb.AppendLine("        public static int Main(string[] args)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (args.Length == 0) { Console.Error.WriteLine(\"Usage: <input>\"); return 1; }");

        // io-shell gets args[0] (C# string), Dafny gets UnicodeFromString
        var inputArgs = SafeName("args");
        var hasIoShell = comp.Classification == ModuleClassification.IoShell;

        if (hasIoShell)
        {
            sb.AppendLine($"            var {inputArgs} = args[0];");
        }
        else
        {
            sb.AppendLine($"            var {inputArgs} = Dafny.Sequence<Dafny.Rune>.UnicodeFromString(args[0]);");
        }

        AppendConnectionCalls(sb, comp, contract, translated, stubs, inputArgs, className);
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
    }

    private static void EmitNonCliWiring(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translated, Dictionary<string, List<CsMethodSignature>> stubs)
    {
        // io-shell non-CLI: skip __default entry call — just expose connections
        foreach (var conn in comp.Connections)
        {
            AppendSingleConnection(sb, conn, contract, translated, stubs, "input");
        }
    }

    private static void AppendConnectionCalls(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translated, Dictionary<string, List<CsMethodSignature>> stubs,
        string entryVar, string className)
    {
        // DETERMINISTIC LINEAR CHAINING:
        // The pipeline is a linear chain. Output of step N feeds into step N+1.
        // The first call gets args[0] (CLI input). Subsequent calls get the previous return.
        // Remaining params get defaults. The model's argMappings are used as hints
        // for non-first params only (e.g. delimiter=",").
        // This avoids trusting the model to name intermediate variables correctly.

        var prevRet = entryVar;  // starts as CLI input, then becomes previous return
        var prevType = comp.Classification == ModuleClassification.IoShell ? "string" : "Dafny.ISequence<Dafny.Rune>";
        var retVarCounter = 0;

        for (var ci = 0; ci < comp.Connections.Length; ci++)
        {
            var conn = comp.Connections[ci];
            var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
            if (targetComp == null) continue;

            // Find the actual C# method signature
            var targetSigs = GetSignatures(targetComp, translated, stubs);
            if (targetSigs == null || targetSigs.Count == 0)
            {
                sb.AppendLine($"            // SKIP: no signatures found for {conn.ToComponent}.{conn.ToMethod}");
                continue;
            }

            var targetSig = ResolveMethod(conn, targetComp, targetSigs);
            var actualMethodName = targetSig.Name;
            var targetClass = ResolveTargetClass(targetComp, translated, stubs);
            var retVarName = $"ret{retVarCounter++}";
            var retType = targetSig.ReturnType;

            // Build args: first param gets the chained previous return,
            // remaining params use argMappings as hints, then defaults
            var args = BuildChainedArgs(conn, prevRet, prevType, targetSig);

            // Dafny multi-return translates to void + out params.
            // The first out param is the data return (chained to next step).
            if (retType == "void" || retType == "Void")
            {
                if (targetSig.OutParamTypes.Length > 0)
                {
                    // Emit out variables for each out param
                    var outVars = new List<string>();
                    for (var oi = 0; oi < targetSig.OutParamTypes.Length; oi++)
                    {
                        var outVar = $"out{retVarCounter}_{oi}";
                        outVars.Add($"out {targetSig.OutParamTypes[oi]} {outVar}");
                    }
                    sb.AppendLine($"            {targetClass}.{actualMethodName}({args}, {string.Join(", ", outVars)});");
                    // Chain the first out param as the return for next step
                    prevRet = $"out{retVarCounter}_0";
                    prevType = targetSig.OutParamTypes[0];

                    // If there is a bool out param (isValid), emit a validation branch.
                    // If validation fails, print error to stderr and exit non-zero.
                    for (var bi = 0; bi < targetSig.OutParamTypes.Length; bi++)
                    {
                        if (targetSig.OutParamTypes[bi] == "bool")
                        {
                            var boolVar = $"out{retVarCounter}_{bi}";
                            sb.AppendLine($"            if (!{boolVar})");
                            sb.AppendLine("            {");
                            sb.AppendLine("                Console.Error.WriteLine(\"Validation failed\");");
                            sb.AppendLine("                return 1;");
                            sb.AppendLine("            }");
                            break;
                        }
                    }
                    retVarCounter++;
                }
                else
                {
                    sb.AppendLine($"            {targetClass}.{actualMethodName}({args});");
                }
            }
            else
            {
                sb.AppendLine($"            var {retVarName} = {targetClass}.{actualMethodName}({args});");
                prevRet = retVarName;
                prevType = retType;
            }
        }
    }

    /// <summary>Build args for linear chaining: first param = previous return, rest from mappings/defaults.</summary>
    private static string BuildChainedArgs(ConnectionSpec conn, string prevRet, string prevType, CsMethodSignature targetSig)
    {
        var parts = new List<string>();
        for (var i = 0; i < targetSig.ParamNames.Length; i++)
        {
            if (i == 0)
            {
                // First param: the chained previous return (with type conversion)
                var converted = ConvertType(prevType, targetSig.ParamTypes[i], prevRet);
                parts.Add(converted ?? DefaultForType(targetSig.ParamTypes[i]));
            }
            else
            {
                // Remaining params: try argMappings, then defaults
                if (i < conn.ArgMappings.Length)
                {
                    var mapping = conn.ArgMappings[i];
                    var arrowIdx = mapping.IndexOf("->");
                    var targetName = arrowIdx >= 0 ? mapping[(arrowIdx + 2)..].Trim() : "";
                    // If the mapping target matches this param name, use the source value
                    if (targetName == targetSig.ParamNames[i] && arrowIdx >= 0)
                    {
                        var sourceField = mapping[..arrowIdx].Trim();
                        // Source could be a literal or a known var — for now, use as string literal
                        if (sourceField.Length > 0)
                            parts.Add($"\"{sourceField}\"");
                        else
                            parts.Add(DefaultForType(targetSig.ParamTypes[i]));
                    }
                    else
                    {
                        parts.Add(DefaultForType(targetSig.ParamTypes[i]));
                    }
                }
                else
                {
                    parts.Add(DefaultForType(targetSig.ParamTypes[i]));
                }
            }
        }
        return string.Join(", ", parts);
    }

    /// <summary>Resolve a method from the connection's toMethod, trying multiple strategies.</summary>
    private static CsMethodSignature ResolveMethod(ConnectionSpec conn, Component targetComp, List<CsMethodSignature> targetSigs)
    {
        // Try exact match on toMethod
        var sig = targetSigs.FirstOrDefault(s => s.Name == conn.ToMethod);
        // Try patternMethod mapping from contract
        if (sig == null)
        {
            var contractSig = targetComp.MethodSignatures.FirstOrDefault(
                m => m.Name == conn.ToMethod && !string.IsNullOrWhiteSpace(m.PatternMethod));
            if (contractSig != null)
                sig = targetSigs.FirstOrDefault(s => s.Name == contractSig.PatternMethod);
        }
        // Fuzzy match
        if (sig == null)
        {
            var toMethodLower = conn.ToMethod.ToLowerInvariant();
            sig = targetSigs.FirstOrDefault(s =>
                s.Name.ToLowerInvariant().Contains(toMethodLower) ||
                toMethodLower.Contains(s.Name.ToLowerInvariant()));
        }
        // Prefer non-factory methods
        if (sig == null)
        {
            sig = targetSigs.FirstOrDefault(s =>
                !s.Name.StartsWith("create_") && !s.Name.StartsWith("_") &&
                s.Name != "Default" && s.Name != "_TypeDescriptor");
        }
        // Last resort
        return sig ?? targetSigs[0];
    }

    private static void AppendSingleConnection(
        StringBuilder sb, ConnectionSpec conn, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> translated, Dictionary<string, List<CsMethodSignature>> stubs,
        string entryVar)
    {
        var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (targetComp == null) return;
        var targetSigs = GetSignatures(targetComp, translated, stubs);
        var targetSig = targetSigs?.FirstOrDefault(s => s.Name == conn.ToMethod);
        if (targetSig == null) return;

        var targetClass = ResolveTargetClass(targetComp, translated, stubs);
        var currentVars = new Dictionary<string, VarInfo> { [entryVar] = new(entryVar, "string") };
        var args = BuildArgs(conn, currentVars, targetSig);

        sb.AppendLine($"            var result = {targetClass}.{conn.ToMethod}({args});");
    }

    private static string BuildArgs(ConnectionSpec conn, Dictionary<string, VarInfo> vars, CsMethodSignature targetSig)
    {
        var parts = new List<string>();
        // Fill ALL parameters of the target method, not just up to argMappings length
        for (var i = 0; i < targetSig.ParamNames.Length; i++)
        {
            // If there's an argMapping for this position, use it
            if (i < conn.ArgMappings.Length)
            {
                var mapping = conn.ArgMappings[i];
                var arrowIdx = mapping.IndexOf("->");
                var sourceField = arrowIdx >= 0 ? mapping[..arrowIdx].Trim() : mapping.Trim();

                if (vars.TryGetValue(sourceField, out var varInfo))
                {
                    var converted = ConvertType(varInfo.CsType, targetSig.ParamTypes[i], varInfo.Name);
                    parts.Add(converted ?? DefaultForType(targetSig.ParamTypes[i]));
                    continue;
                }
            }
            // No mapping or mapping not found: use default for this param type
            parts.Add(DefaultForType(targetSig.ParamTypes[i]));
        }
        return string.Join(", ", parts);
    }

    /// <summary>Convert types at Dafny/io-shell boundary. Returns null if incompatible.</summary>
    public static string? ConvertType(string fromType, string toType, string varName)
    {
        if (fromType == toType) return varName;
        // Count ISequence nesting depth (not IndexOf — that breaks on "Dafny." prefix)
        int toDepth = CountSeq(toType);
        int fromDepth = CountSeq(fromType);
        bool toHasRune = toType.Contains("Rune");
        bool fromHasRune = fromType.Contains("Rune");
        // string -> ISequence<Rune> (1D): UnicodeFromString
        if (toDepth == 1 && toHasRune && fromType == "string")
            return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({varName})";
        // ISequence<Rune> -> string: iterate runes to build string
        if (fromDepth == 1 && fromHasRune && toType == "string")
            return $"new string({varName}.Select(r => (char)r.Value).ToArray())";
        // string[] (io-shell ReadLines) -> ISequence<ISequence<Rune>> (2D): array→seq mapping
        if (fromType == "string[]" && toDepth == 2 && toHasRune)
            return $"Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromArray({varName}.Select(s => Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)).ToArray())";
        // ISequence<ISequence<Rune>> (2D) -> string[]: seq→array mapping
        if (toType == "string[]" && fromDepth == 2 && fromHasRune)
            return $"{varName}.Select(seq => new string(seq.Select(r => (char)r.Value).ToArray())).ToArray()";
        // Dimensionality upgrades from scalar string are SEMANTICALLY WRONG.
        if (toDepth >= 2 && fromType == "string")
            throw new InvalidOperationException(
                $"ConvertType: dimensionality upgrade {fromType} -> {toType} not supported. "
                + $"Use ReadLines stub (returns seq<string>) instead of ReadFile. Variable: {varName}");
        if (fromDepth == 1 && fromHasRune && toDepth >= 2)
            throw new InvalidOperationException(
                $"ConvertType: dimensionality upgrade {fromType} -> {toType} not supported. "
                + $"Stubs must return the right shape. Variable: {varName}");
        // Compatible if same base type
        if (fromType == toType) return varName;
        return null; // incompatible
    }

    /// <summary>Rename 'args' to 'inputArgs' (NOT @args). @ is just a prefix.</summary>
    public static string SafeName(string name) => name == "args" ? "inputArgs" : name;

    /// <summary>Count ISequence nesting depth (not IndexOf — that breaks on "Dafny." prefix).</summary>
    private static int CountSeq(string type)
    {
        int count = 0, idx = 0;
        while ((idx = type.IndexOf("ISequence", idx)) >= 0) { count++; idx += 9; }
        return count;
    }

    /// <summary>Resolve actual class name from scanner (Frame vs __default vs stub class).</summary>
    private static string ResolveDafnyClass(Component comp, Dictionary<string, List<CsMethodSignature>> sigs)
    {
        if (sigs.TryGetValue(comp.Name, out var methods) && methods.Count > 0)
        {
            var m = methods[0];
            // Return fully-qualified name: namespace.class (e.g. "_module.__default")
            // Without the namespace prefix, the compiler can't find the class.
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return m.ClassName;
        }
        return comp.Classification == ModuleClassification.IoShell ? comp.Name : "_module." + comp.Name;
    }

    /// <summary>Resolve target class name, checking stub signatures for io-shell components.</summary>
    private static string ResolveTargetClass(Component targetComp,
        Dictionary<string, List<CsMethodSignature>> translated,
        Dictionary<string, List<CsMethodSignature>> stubs)
    {
        // For io-shell: check stub signatures first (class is ConsoleIO, FileIO, etc.)
        if (targetComp.Classification == ModuleClassification.IoShell &&
            stubs.TryGetValue(targetComp.Name, out var stubMethods) && stubMethods.Count > 0)
        {
            var m = stubMethods[0];
            // Stub classes are in namespace {comp.Name}, so just use ClassName
            // e.g. JsonOutput.ConsoleIO
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return $"{targetComp.Name}.{m.ClassName}";
        }
        // For dafny: use translated signatures
        return ResolveDafnyClass(targetComp, translated);
    }

    private static List<CsMethodSignature>? GetSignatures(
        Component comp,
        Dictionary<string, List<CsMethodSignature>> translated,
        Dictionary<string, List<CsMethodSignature>> stubs) =>
        translated.TryGetValue(comp.Name, out var t) ? t
            : (stubs.TryGetValue(comp.Name, out var s) ? s : null);

    private static string DefaultForType(string type) =>
        type switch
        {
            "int" or "BigInteger" or "long" => "0",
            "bool" => "false",
            "string" => "\"\"",
            // Dafny string type — use comma as default delimiter (most common CSV case)
            _ when type.Contains("ISequence") && type.Contains("Rune")
                && type.IndexOf("ISequence", type.IndexOf("ISequence") + 1) < 0
                => "Dafny.Sequence<Dafny.Rune>.UnicodeFromString(\",\")",
            // Dafny seq<seq<string>> — empty sequence
            _ when type.Contains("ISequence") && type.IndexOf("ISequence", type.IndexOf("ISequence") + 1) > 0
                => "Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Empty",
            _ => $"default({type})"
        };
}