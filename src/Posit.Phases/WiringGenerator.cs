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
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");

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
            // Multi-token stdin (T6 a2): '32 F' = value + unit(s). Split once;
            // consumers index tokens[] by parameter position.
            sb.AppendLine("            var tokens = inputLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);");
        }
        else
        {
            sb.AppendLine("            if (args.Length == 0) { Console.Error.WriteLine(\"Usage: <input>\"); return 1; }");
        }

        sb.AppendLine("            try");
        sb.AppendLine("            {");
        AppendConnectionCalls(sb, comp, contract, impl, stubs, isStdin ? "inputLine" : "args[0]");
        sb.AppendLine("                return 0;");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (System.IO.FileNotFoundException ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                Console.Error.WriteLine($\"Error: file not found: {ex.Message}\");");
        sb.AppendLine("                return 1;");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                Console.Error.WriteLine($\"Error: {ex.Message}\");");
        sb.AppendLine("                return 1;");
        sb.AppendLine("            }");
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
            else if (ci == 0 &&
                     targetSig.ParamTypes.Length >= 1 &&
                     targetSig.ParamTypes
                         .All(p => NormalizeType(p) == "string[]" || NormalizeType(p) == "string"))
            {
                // Architect connected Run → logic directly and the logic takes
                // file-shaped params (string content or string[] lines, any count).
                // For each string[] param: read that arg's file as LINES (T3 a1);
                // for each string param: read that arg's file as CONTENT (T12).
                // args[i] for i-th param — deterministic, no stub hop needed.
                var argCount = targetSig.ParamTypes.Length;
                var argExprs = new List<string>();
                for (var ai = 0; ai < argCount; ai++)
                {
                    var pt = NormalizeType(targetSig.ParamTypes[ai]);
                    if (pt == "string[]")
                    {
                        var lv = $"linesArg{ai}";
                        sb.AppendLine($"            var {lv} = args.Length > {ai} ? System.IO.File.ReadAllLines(args[{ai}]) : Array.Empty<string>();");
                        argExprs.Add(lv);
                    }
                    else
                    {
                        var fv = $"fileContent{ai}";
                        sb.AppendLine($"            var {fv} = args.Length > {ai} ? System.IO.File.ReadAllText(args[{ai}]) : \"\";");
                        argExprs.Add(fv);
                    }
                }
                var isInstanceTarget = targetComp.Classification != ModuleClassification.IoShell;
                var receiver = targetClass;
                if (isInstanceTarget)
                {
                    var instanceVar = $"inst_{conn.ToComponent}";
                    if (!sb.ToString().Contains($"var {instanceVar} ="))
                        sb.AppendLine($"            var {instanceVar} = new global::{targetClass.Replace('.', '.')}();".Replace("global::global::", "global::"));
                    receiver = instanceVar;
                }
                sb.AppendLine($"            var ret{retVarCounter} = {receiver}.{targetSig.Name}({string.Join(", ", argExprs)});");
                EmitPrint(sb, targetSig.ReturnType, $"ret{retVarCounter}", isLast: ci == comp.Connections.Length - 1);
                // Chain state MUST advance here — a subsequent connection (e.g.
                // MergeConfigs(ParseIni(...), ...)) feeds on this return value.
                // Missing this made the T12 corpus case emit args[0] instead of ret0.
                prevRet = $"ret{retVarCounter}";
                prevType = targetSig.ReturnType;
                retVarCounter++;
                continue;
            }

            // Build args: first param = chained previous return, rest from mappings/defaults
            var args = BuildChainedArgs(conn, prevRet, prevType, targetSig);
            var retVoid = retType == "void" || retType == "Void";

            // Logic components are instance classes — need to instantiate.
            // Io-shell stubs are static classes — call directly.
            var isInstance = targetComp.Classification != ModuleClassification.IoShell;
            var callTarget = targetClass;
            if (isInstance)
            {
                var instanceVar = $"inst_{conn.ToComponent}";
                // Only declare once per component
                if (!sb.ToString().Contains($"var {instanceVar} ="))
                    sb.AppendLine($"            var {instanceVar} = new {targetClass}();");
                callTarget = instanceVar;
            }

            if (retVoid)
            {
                sb.AppendLine($"            {callTarget}.{targetSig.Name}({args});");
            }
            else
            {
                sb.AppendLine($"            var {retVarName} = {callTarget}.{targetSig.Name}({args});");

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
                    // Print result if this is the last connection (final output).
                    // Error-string convention lives inside EmitPrint (both paths).
                    if (ci == comp.Connections.Length - 1)
                        EmitPrint(sb, retType, retVarName, isLast: true);
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
                // Stdin entry: first param may be a NUMBER parsed from token 0
                // ('32 F' → 32), not the raw line. Raw line only fits string params.
                if (prevRet == "inputLine" &&
                    (targetSig.ParamTypes[i] == "double" || targetSig.ParamTypes[i] == "int" || targetSig.ParamTypes[i] == "long"))
                {
                    parts.Add($"{targetSig.ParamTypes[i]}.Parse(tokens[0])");
                    continue;
                }
                var converted = ConvertType(prevType, targetSig.ParamTypes[i], prevRet);
                // Never silently default the FIRST chained param: if conversion
                // fails the chain is broken — emit the raw var and let the
                // compiler complain (fixable) instead of wrong behavior. Raw var
                // works whenever the types actually match (normalized compare).
                parts.Add(converted ?? prevRet);
            }
            else if (prevType == "string" &&
                     (prevRet == "inputLine" || prevRet == "stdinLine") &&
                     targetSig.ParamTypes[i] == "string" &&
                     !IsLiteralFilled(i, conn, targetSig))
            {
                // Stdin "value unit" decomposition (T6 a2): a stdin line like
                // '32 F' carries N whitespace-separated tokens; token 0 went to
                // the first param, token i goes to this string param. Prevents
                // the empty-"" convention from discarding real units.
                parts.Add($"tokens[{i}]");
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
        // Normalize whitespace so "Dictionary<string, string>" (interface) matches
        // "Dictionary<string,string>" (scanner) — whitespace-blind type compare.
        var from = NormalizeType(fromType);
        var to = NormalizeType(toType);
        if (from == to) return varName;

        // string → int
        if (from == "string" && to == "int")
            return $"int.Parse({varName})";

        // string → long
        if (from == "string" && to == "long")
            return $"long.Parse({varName})";

        // string → double
        if (from == "string" && to == "double")
            return $"double.Parse({varName})";

        // string → bool
        if (from == "string" && to == "bool")
            return $"bool.Parse({varName})";

        // string[] → string (join with newlines)
        if (from == "string[]" && to == "string")
            return $"string.Join(\"\\n\", {varName})";

        // string → string[] (split by newlines)
        if (from == "string" && to == "string[]")
            return $"{varName}.Split('\\n')";

        // int → string
        if (from == "int" && to == "string")
            return $"{varName}.ToString()";

        // long → string
        if (from == "long" && to == "string")
            return $"{varName}.ToString()";

        // double → string
        if (from == "double" && to == "string")
            return $"{varName}.ToString()";

        // bool → string
        if (from == "bool" && to == "string")
            return $"{varName}.ToString().ToLower()";

        return null; // incompatible
    }

    /// <summary>
    /// Normalize a C# type string for comparison: strip all whitespace and
    /// normalize "Dictionary&lt;K,V&gt;"/"List&lt;T&gt;" casing. The scanner keeps
    /// interface whitespace ("Dictionary&lt;string, string&gt;") while param types
    /// may arrive normalized — the chain must match regardless.
    /// </summary>
    public static string NormalizeType(string type)
    {
        if (string.IsNullOrEmpty(type)) return type;
        var t = type.Replace(" ", "").Replace("\t", "");
        // Common aliases the scanner produces
        if (t == "System.Collections.Generic.Dictionary<K,V>") t = t[(t.LastIndexOf('.') + 1)..];
        if (t.StartsWith("System.Collections.Generic.")) t = t["System.Collections.Generic.".Length..];
        return t;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Emit the final-output print statement for a return value, typed:
    /// scalars print directly; string[]/List&lt;string&gt; join with newlines;
    /// Dictionary&lt;string,string&gt; prints key=value lines; anything else
    /// (custom classes) is JSON-serialized. Raw WriteLine on ANY non-scalar
    /// prints its TYPE NAME — the T12 'ConfigMerger.MergeResult' failure.
    /// </summary>
    private static void EmitPrint(StringBuilder sb, string retType, string varName, bool isLast)
    {
        if (!isLast) return;
        var t = NormalizeType(retType);
        if (t is "string" or "int" or "long" or "double" or "bool")
        {
            // "Error:" string convention (T3 attempt-2): a string return starting
            // with "Error:" is a failure — stderr + exit 1. Success prints stdout.
            if (t == "string")
            {
                sb.AppendLine($"            if ({varName}.StartsWith(\"Error:\"))");
                sb.AppendLine("            {");
                sb.AppendLine($"                Console.Error.WriteLine({varName});");
                sb.AppendLine("                return 1;");
                sb.AppendLine("            }");
                sb.AppendLine($"            Console.WriteLine({varName});");
            }
            else
                sb.AppendLine($"            Console.WriteLine({varName});");
        }
        else if (t is "string[]" or "List<string>")
        {
            // Empty collection → print NOTHING: string.Join on [] returns "",
            // but WriteLine adds a bare '\n' that breaks byte-exact QA keys
            // expecting empty output (T12 tc3: merged-empty → stdout truly empty).
            var countProp = t == "string[]" ? "Length" : "Count";
            sb.AppendLine($"            if ({varName}.{countProp} > 0) Console.WriteLine(string.Join(\"\\n\", {varName}));");
        }
        else if (t == "Dictionary<string,string>")
            sb.AppendLine($"            if ({varName}.Count > 0) Console.WriteLine(string.Join(\"\\n\", {varName}.Select(kv => kv.Key + \"=\" + kv.Value)));");
        else
            sb.AppendLine($"            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize({varName}));");
    }

    /// <summary>
    /// True for types Console.WriteLine prints usefully: primitives, enums of
    /// primitives, string[], List&lt;string&gt;, Dictionary whose VALUES are
    /// primitives. Custom classes (MergeResult, Foo) → false → serialize JSON.
    /// </summary>
    private static bool IsSimpleCollection(string type)
    {
        var t = NormalizeType(type);
        if (t == "string[]" || t == "List<string>") return true;
        if (t.StartsWith("Dictionary<string,string>")) return true;
        if (t.StartsWith("List<") && t.EndsWith(">"))
        {
            var el = t[5..^1];
            return el is "string" or "int" or "long" or "double" or "bool";
        }
        return false;
    }

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
            var ns = string.IsNullOrEmpty(m.Namespace) ? targetComp.Name : m.Namespace;
            // global:: qualifier: survives the namespace==classname collision
            // (namespace LogAnalyzer { class LogAnalyzer {} }) where a plain
            // "LogAnalyzer.LogAnalyzer" fails CS0234 because the name binds to
            // the class first (T8 corpus failure).
            return $"global::{ns}.{m.ClassName}";
        }
        // logic: use impl signatures
        if (impl.TryGetValue(targetComp.Name, out var methods) && methods.Count > 0)
        {
            var m = methods[0];
            var ns = string.IsNullOrEmpty(m.Namespace) ? targetComp.Name : m.Namespace;
            return $"global::{ns}.{m.ClassName}";
        }
        return $"global::{targetComp.Name}.{targetComp.Name}";
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

    /// <summary>
    /// True when param i receives an explicit literal via argMappings (architect's
    /// routing decision) — token decomposition must not override it.
    /// </summary>
    private static bool IsLiteralFilled(int paramIndex, ConnectionSpec conn, CsMethodSignature targetSig)
    {
        if (paramIndex >= conn.ArgMappings.Length) return false;
        var mapping = conn.ArgMappings[paramIndex];
        var arrowIdx = mapping.IndexOf("->");
        return arrowIdx >= 0 && mapping[(arrowIdx + 2)..].Trim() == targetSig.ParamNames[paramIndex];
    }
}