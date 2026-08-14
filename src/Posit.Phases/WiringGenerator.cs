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
        var currentVars = new Dictionary<string, VarInfo> { [entryVar] = new(entryVar, "string") };
        var retVarCounter = 0;

        foreach (var conn in comp.Connections)
        {
            var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
            if (targetComp == null) continue;

            var targetSigs = GetSignatures(targetComp, translated, stubs);
            var targetSig = targetSigs?.FirstOrDefault(s => s.Name == conn.ToMethod);
            if (targetSig == null) continue;

            var targetClass = ResolveDafnyClass(targetComp, translated);
            var retVarName = $"ret{retVarCounter++}";
            var retType = targetSig.ReturnType;

            // Build arguments from arg mappings
            var args = BuildArgs(conn, currentVars, targetSig);

            sb.AppendLine($"            var {retVarName} = {targetClass}.{conn.ToMethod}({args});");
            currentVars[retVarName] = new VarInfo(retVarName, retType);
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

        var targetClass = ResolveDafnyClass(targetComp, translated);
        var currentVars = new Dictionary<string, VarInfo> { [entryVar] = new(entryVar, "string") };
        var args = BuildArgs(conn, currentVars, targetSig);

        sb.AppendLine($"            var result = {targetClass}.{conn.ToMethod}({args});");
    }

    private static string BuildArgs(ConnectionSpec conn, Dictionary<string, VarInfo> vars, CsMethodSignature targetSig)
    {
        var parts = new List<string>();
        for (var i = 0; i < conn.ArgMappings.Length && i < targetSig.ParamNames.Length; i++)
        {
            var mapping = conn.ArgMappings[i];
            var arrowIdx = mapping.IndexOf("->");
            var sourceField = arrowIdx >= 0 ? mapping[..arrowIdx].Trim() : mapping.Trim();
            var paramName = targetSig.ParamNames[i];

            if (vars.TryGetValue(sourceField, out var varInfo))
            {
                var converted = ConvertType(varInfo.CsType, targetSig.ParamTypes[i], varInfo.Name);
                parts.Add(converted ?? DefaultForType(targetSig.ParamTypes[i]));
            }
            else
            {
                parts.Add(DefaultForType(targetSig.ParamTypes[i]));
            }
        }
        return string.Join(", ", parts);
    }

    /// <summary>Convert types at Dafny/io-shell boundary. Returns null if incompatible.</summary>
    public static string? ConvertType(string fromType, string toType, string varName)
    {
        if (fromType == toType) return varName;
        // string -> ISequence<Rune>: UnicodeFromString
        if (toType.Contains("ISequence") && toType.Contains("Rune") && fromType == "string")
            return $"Dafny.Sequence<Dafny.Rune>.UnicodeFromString({varName})";
        // ISequence<Rune> -> string: SequenceToString
        if (fromType.Contains("ISequence") && fromType.Contains("Rune") && toType == "string")
            return $"Dafny.Helpers.SequenceToString({varName})";
        // Compatible if same base type
        if (fromType == toType) return varName;
        return null; // incompatible
    }

    /// <summary>Rename 'args' to 'inputArgs' (NOT @args). @ is just a prefix.</summary>
    public static string SafeName(string name) => name == "args" ? "inputArgs" : name;

    /// <summary>Resolve actual class name from scanner (Frame vs __default).</summary>
    private static string ResolveDafnyClass(Component comp, Dictionary<string, List<CsMethodSignature>> sigs)
    {
        if (sigs.TryGetValue(comp.Name, out var methods) && methods.Count > 0)
            return methods[0].ClassName;
        return comp.Classification == ModuleClassification.IoShell ? comp.Name : "_module." + comp.Name;
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