namespace Posit.Phases;

/// <summary>Variable info for wiring — name and C# type.</summary>
public record VarInfo(string Name, string CsType);

/// <summary>
/// Generates Wire.cs files for the C#-direct pipeline.
/// Reads C# interface signatures (not Dafny translated types).
/// Native C# types only — no Dafny runtime, no ISequence, no UnicodeFromString.
/// </summary>
public static class WiringGenerator
{
    /// <summary>
    /// Generate Wire.cs for a component with connections.
    /// </summary>
    public static string Generate(
        Component comp,
        ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> implSignatures,
        Dictionary<string, List<CsMethodSignature>> stubSignatures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// {comp.Name}/Wire.cs — auto-generated wiring");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");

        // Add using directives for each referenced component's namespace
        var referencedComponents = new HashSet<string>();
        foreach (var conn in comp.Connections)
        {
            if (conn.ToComponent != comp.Name)
                referencedComponents.Add(conn.ToComponent);
        }
        foreach (var refComp in referencedComponents.OrderBy(c => c))
            sb.AppendLine($"using {refComp};");

        sb.AppendLine();
        sb.AppendLine($"namespace {comp.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    public static class Wire");
        sb.AppendLine("    {");

        var isCli = comp.Connections.Length > 0;
        if (isCli)
            EmitCliWiring(sb, comp, contract, implSignatures, stubSignatures);
        else
            EmitNonCliWiring(sb, comp, contract, implSignatures, stubSignatures);

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── CLI wiring ─────────────────────────────────────────────────────────────

    private static void EmitCliWiring(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> impl, Dictionary<string, List<CsMethodSignature>> stubs)
    {
        sb.AppendLine("        public static int Main(string[] args)");
        sb.AppendLine("        {");

        var entryType = comp.EntryType ?? "file";
        var isStdin = entryType.Equals("stdin", StringComparison.OrdinalIgnoreCase);

        // Entry: read input (file path from args[0], or stdin line)
        if (isStdin)
        {
            sb.AppendLine("            var inputLine = Console.ReadLine() ?? \"\";");
            sb.AppendLine("            if (string.IsNullOrEmpty(inputLine)) { Console.Error.WriteLine(\"Error: no input provided.\"); return 1; }");
        }
        else
        {
            sb.AppendLine("            if (args.Length == 0) { Console.Error.WriteLine(\"Usage: <input>\"); return 1; }");
        }

        AppendConnectionCalls(sb, comp, contract, impl, stubs, isStdin ? "inputLine" : "args[0]");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
    }

    private static void EmitNonCliWiring(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> impl, Dictionary<string, List<CsMethodSignature>> stubs)
    {
        foreach (var conn in comp.Connections)
            AppendSingleConnection(sb, conn, contract, impl, stubs, "input");
    }

    // ── Connection chaining ────────────────────────────────────────────────────

    private static void AppendConnectionCalls(
        StringBuilder sb, Component comp, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> impl, Dictionary<string, List<CsMethodSignature>> stubs,
        string entryVar)
    {
        // Linear chaining: output of step N feeds into step N+1.
        var prevRet = entryVar;
        var prevType = "string";
        var retVarCounter = 0;

        for (var ci = 0; ci < comp.Connections.Length; ci++)
        {
            var conn = comp.Connections[ci];
            var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
            if (targetComp == null) continue;

            var targetSigs = GetSignatures(targetComp, impl, stubs);
            if (targetSigs == null || targetSigs.Count == 0)
            {
                sb.AppendLine($"            // SKIP: no signatures found for {conn.ToComponent}.{conn.ToMethod}");
                continue;
            }

            var targetSig = ResolveMethod(conn, targetComp, targetSigs);
            var targetClass = ResolveTargetClass(targetComp, impl, stubs);
            var retVarName = $"ret{retVarCounter++}";
            var retType = targetSig.ReturnType;

            // Stub call (ReadLines, ReadFile) transforms input on first connection
            var stubMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ReadLines", "ReadFile", "ReadStdin"
            };
            if (stubMethods.Contains(conn.FromMethod) && ci == 0)
            {
                if (conn.FromMethod.Equals("ReadLines", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"            var linesResult = System.IO.File.ReadAllLines({prevRet});");
                    prevRet = "linesResult";
                    prevType = "string[]";
                }
                else if (conn.FromMethod.Equals("ReadFile", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"            var fileResult = System.IO.File.ReadAllText({prevRet});");
                    prevRet = "fileResult";
                    prevType = "string";
                }
            }

            // Build args: first param = chained previous return, rest from mappings/defaults
            var args = BuildChainedArgs(conn, prevRet, prevType, targetSig);
            var retVoid = retType == "void" || retType == "Void";

            if (retVoid)
            {
                sb.AppendLine($"            {targetClass}.{targetSig.Name}({args});");
            }
            else
            {
                sb.AppendLine($"            var {retVarName} = {targetClass}.{targetSig.Name}({args});");

                // If return type is bool (validation result), emit error branch
                if (retType == "bool")
                {
                    sb.AppendLine($"            if (!{retVarName})");
                    sb.AppendLine("            {");
                    sb.AppendLine("                Console.Error.WriteLine(\"Validation failed\");");
                    sb.AppendLine("                return 1;");
                    sb.AppendLine("            }");
                }
                else
                {
                    // Print result if this is the last connection (final output)
                    if (ci == comp.Connections.Length - 1)
                        sb.AppendLine($"            Console.WriteLine({retVarName});");
                }

                prevRet = retVarName;
                prevType = retType;
            }
        }
    }

    private static string BuildChainedArgs(ConnectionSpec conn, string prevRet, string prevType, CsMethodSignature targetSig)
    {
        var parts = new List<string>();
        for (var i = 0; i < targetSig.ParamNames.Length; i++)
        {
            if (i == 0)
            {
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
                    if (targetName == targetSig.ParamNames[i] && arrowIdx >= 0)
                    {
                        var sourceField = mapping[..arrowIdx].Trim();
                        parts.Add(sourceField.Length > 0 ? $"\"{sourceField}\"" : DefaultForType(targetSig.ParamTypes[i]));
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

    // ── Method resolution ──────────────────────────────────────────────────────

    private static CsMethodSignature ResolveMethod(ConnectionSpec conn, Component targetComp, List<CsMethodSignature> targetSigs)
    {
        // Exact match
        var sig = targetSigs.FirstOrDefault(s => s.Name == conn.ToMethod);
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
        return sig ?? targetSigs[0];
    }

    private static void AppendSingleConnection(
        StringBuilder sb, ConnectionSpec conn, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> impl, Dictionary<string, List<CsMethodSignature>> stubs,
        string entryVar)
    {
        var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (targetComp == null) return;
        var targetSigs = GetSignatures(targetComp, impl, stubs);
        var targetSig = targetSigs?.FirstOrDefault(s => s.Name == conn.ToMethod);
        if (targetSig == null) return;

        var targetClass = ResolveTargetClass(targetComp, impl, stubs);
        var args = BuildChainedArgs(conn, entryVar, "string", targetSig);
        sb.AppendLine($"            var result = {targetClass}.{conn.ToMethod}({args});");
    }

    // ── Type conversion (native C# only) ───────────────────────────────────────

    /// <summary>Convert between native C# types. Returns null if incompatible.</summary>
    public static string? ConvertType(string fromType, string toType, string varName)
    {
        if (fromType == toType) return varName;

        // string → int
        if (fromType == "string" && toType == "int")
            return $"int.Parse({varName})";

        // string → long
        if (fromType == "string" && toType == "long")
            return $"long.Parse({varName})";

        // string → double
        if (fromType == "string" && toType == "double")
            return $"double.Parse({varName})";

        // string → bool
        if (fromType == "string" && toType == "bool")
            return $"bool.Parse({varName})";

        // string[] → string (join with newlines)
        if (fromType == "string[]" && toType == "string")
            return $"string.Join(\"\\n\", {varName})";

        // string → string[] (split by newlines)
        if (fromType == "string" && toType == "string[]")
            return $"{varName}.Split('\\n')";

        // int → string
        if (fromType == "int" && toType == "string")
            return $"{varName}.ToString()";

        // long → string
        if (fromType == "long" && toType == "string")
            return $"{varName}.ToString()";

        // double → string
        if (fromType == "double" && toType == "string")
            return $"{varName}.ToString()";

        // bool → string
        if (fromType == "bool" && toType == "string")
            return $"{varName}.ToString().ToLower()";

        return null; // incompatible
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    public static string SafeName(string name) => name == "args" ? "inputArgs" : name;

    private static string ResolveTargetClass(Component targetComp,
        Dictionary<string, List<CsMethodSignature>> impl,
        Dictionary<string, List<CsMethodSignature>> stubs)
    {
        // io-shell: check stub signatures
        if (targetComp.Classification == ModuleClassification.IoShell &&
            stubs.TryGetValue(targetComp.Name, out var stubMethods) && stubMethods.Count > 0)
        {
            var m = stubMethods[0];
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return $"{targetComp.Name}.{m.ClassName}";
        }
        // logic: use impl signatures
        if (impl.TryGetValue(targetComp.Name, out var methods) && methods.Count > 0)
        {
            var m = methods[0];
            if (!string.IsNullOrEmpty(m.Namespace))
                return $"{m.Namespace}.{m.ClassName}";
            return $"{targetComp.Name}.{m.ClassName}";
        }
        return $"{targetComp.Name}.{targetComp.Name}";
    }

    private static List<CsMethodSignature>? GetSignatures(
        Component comp,
        Dictionary<string, List<CsMethodSignature>> impl,
        Dictionary<string, List<CsMethodSignature>> stubs) =>
        impl.TryGetValue(comp.Name, out var t) ? t
            : (stubs.TryGetValue(comp.Name, out var s) ? s : null);

    private static string DefaultForType(string type) =>
        type switch
        {
            "int" or "long" => "0",
            "double" => "0.0",
            "bool" => "false",
            "string" => "\"\"",
            "string[]" => "Array.Empty<string>()",
            _ => $"default({type})"
        };
}