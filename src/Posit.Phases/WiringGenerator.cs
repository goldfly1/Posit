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
        // Variable registry: maps semantic names → actual C# variable names
        // The entry var is registered under both its own name and common aliases
        var currentVars = new Dictionary<string, VarInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [entryVar] = new(entryVar, "string"),
            ["input"] = new(entryVar, "string"),
            ["args"] = new(entryVar, "string"),
        };
        var retVarCounter = 0;

        // Build a map of argMapping source names → which ret var they refer to
        // by scanning ahead: each connection's return value gets registered
        // under the source names that future connections expect

        for (var ci = 0; ci < comp.Connections.Length; ci++)
        {
            var conn = comp.Connections[ci];
            var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
            if (targetComp == null) continue;

            // Find the actual C# method signature — try exact name match, then patternMethod
            var targetSigs = GetSignatures(targetComp, translated, stubs);
            if (targetSigs == null || targetSigs.Count == 0)
            {
                sb.AppendLine($"            // SKIP: no signatures found for {conn.ToComponent}.{conn.ToMethod}");
                continue;
            }

            // Try exact match on toMethod
            var targetSig = targetSigs.FirstOrDefault(s => s.Name == conn.ToMethod);
            // If not found, try matching via the contract's MethodSignatures patternMethod
            if (targetSig == null)
            {
                var contractSig = targetComp.MethodSignatures.FirstOrDefault(
                    m => m.Name == conn.ToMethod && !string.IsNullOrWhiteSpace(m.PatternMethod));
                if (contractSig != null)
                    targetSig = targetSigs.FirstOrDefault(s => s.Name == contractSig.PatternMethod);
            }
            // If still not found, try fuzzy match: toMethod contains or is contained in actual method name
            if (targetSig == null)
            {
                var toMethodLower = conn.ToMethod.ToLowerInvariant();
                targetSig = targetSigs.FirstOrDefault(s =>
                    s.Name.ToLowerInvariant().Contains(toMethodLower) ||
                    toMethodLower.Contains(s.Name.ToLowerInvariant()));
            }
            // If still not found, prefer non-factory methods (skip Default, _TypeDescriptor, create_*)
            if (targetSig == null)
            {
                targetSig = targetSigs.FirstOrDefault(s =>
                    !s.Name.StartsWith("create_") && !s.Name.StartsWith("_") &&
                    s.Name != "Default" && s.Name != "_TypeDescriptor");
            }
            // Last resort: first available
            if (targetSig == null)
                targetSig = targetSigs[0];

            // Determine the actual method name to call in C#
            var actualMethodName = targetSig.Name;

            var targetClass = ResolveTargetClass(targetComp, translated, stubs);
            var retVarName = $"ret{retVarCounter++}";
            var retType = targetSig.ReturnType;

            // Build arguments from arg mappings
            var args = BuildArgs(conn, currentVars, targetSig);

            // Emit the call with the ACTUAL C# method name, not the model's semantic name
            if (retType == "void" || retType == "Void")
            {
                sb.AppendLine($"            {targetClass}.{actualMethodName}({args});");
            }
            else
            {
                sb.AppendLine($"            var {retVarName} = {targetClass}.{actualMethodName}({args});");
                // Register return value under retN AND under the source names that
                // future connections will use to reference this return
                currentVars[retVarName] = new VarInfo(retVarName, retType);
                // Look ahead: find the source field names in the NEXT connections
                // that would reference this return value
                for (var ni = ci + 1; ni < comp.Connections.Length; ni++)
                {
                    var nextConn = comp.Connections[ni];
                    foreach (var mapping in nextConn.ArgMappings)
                    {
                        var arrowIdx = mapping.IndexOf("->");
                        var sourceField = arrowIdx >= 0 ? mapping[..arrowIdx].Trim() : mapping.Trim();
                        // If this source field isn't already a known var, register retVar under it
                        if (!currentVars.ContainsKey(sourceField))
                        {
                            currentVars[sourceField] = new VarInfo(retVarName, retType);
                        }
                    }
                }
            }
        }
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
        // string -> ISequence<Rune> (but NOT ISequence<ISequence<Rune>>): UnicodeFromString
        if (toType.Contains("ISequence") && toType.Contains("Rune") && toType.IndexOf("ISequence", 1) < 0
            && fromType == "string")
            return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({varName})";
        // ISequence<Rune> -> string: iterate runes to build string
        if (fromType.Contains("ISequence") && fromType.Contains("Rune") && toType == "string")
            return $"new string({varName}.Select(r => (char)r.Value).ToArray())";
        // string -> ISequence<ISequence<Rune>>: wrap string as single-element seq of seqs
        if (toType.Contains("ISequence") && toType.Contains("ISequence") && toType.Contains("Rune")
            && fromType == "string")
            return $"Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(Dafny.Sequence<Dafny.Rune>.UnicodeFromString({varName}))";
        // ISequence<Rune> -> ISequence<ISequence<Rune>>: wrap as single-element seq
        if (fromType.Contains("ISequence") && fromType.Contains("Rune") && fromType.IndexOf("ISequence", 1) < 0
            && toType.Contains("ISequence") && toType.Contains("ISequence") && toType.Contains("Rune"))
            return $"Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements({varName})";
        // Compatible if same base type
        if (fromType == toType) return varName;
        return null; // incompatible
    }

    /// <summary>Rename 'args' to 'inputArgs' (NOT @args). @ is just a prefix.</summary>
    public static string SafeName(string name) => name == "args" ? "inputArgs" : name;

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
            _ => $"default({type})"
        };
}