namespace Posit.Phases;

/// <summary>
/// Type chain checker. Runs AFTER architecture and AFTER C# implementation.
/// For each consecutive pair of connections on a component, checks that the
/// return type of step N is compatible with the first param of step N+1.
/// If not, returns errors that route back to Architecture via CorrectionSignal.
/// </summary>
public static class TypeChainChecker
{
    /// <summary>
    /// Check the type chain against scanned C# signatures.
    /// Returns list of errors (empty = clean).
    /// </summary>
    public static List<TypeChainError> Check(
        ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> scannedSigs)
    {
        var errors = new List<TypeChainError>();

        foreach (var comp in contract.Components)
        {
            if (comp.Connections.Length < 2) continue;

            for (int i = 0; i < comp.Connections.Length - 1; i++)
            {
                var cur = comp.Connections[i];
                var next = comp.Connections[i + 1];

                var curRetType = GetReturnType(cur, contract, scannedSigs);
                if (curRetType == null) continue;
                if (curRetType == "void" || curRetType == "Void") continue;

                var nextParamType = GetFirstParamType(next, contract, scannedSigs);
                if (nextParamType == null) continue;

                if (!AreCompatible(curRetType, nextParamType))
                {
                    errors.Add(new TypeChainError(
                        comp.Name,
                        i,
                        cur.ToComponent, cur.ToMethod, curRetType,
                        next.ToComponent, next.ToMethod, nextParamType));
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Pre-implementation type chain check. Runs AFTER architecture, BEFORE
    /// C# implementation — the data flow spec check. Uses declared method
    /// signatures from the contract (ReturnType, Params[].Type — both C# notation).
    /// This catches type mismatches before anyone writes code, so the
    /// correction signal routes back to the architect.
    /// </summary>
    public static List<TypeChainError> CheckPreImpl(ArchitectureContract contract)
    {
        var errors = new List<TypeChainError>();

        foreach (var comp in contract.Components)
        {
            if (comp.Connections.Length < 2) continue;

            for (int i = 0; i < comp.Connections.Length - 1; i++)
            {
                var cur = comp.Connections[i];
                var next = comp.Connections[i + 1];

                var curRetType = GetDeclaredReturnType(cur, contract);
                if (curRetType == null) continue;
                if (curRetType == "void" || curRetType == "Void") continue;

                var nextParamType = GetDeclaredFirstParamType(next, contract);
                if (nextParamType == null) continue;

                if (!AreCompatible(curRetType, nextParamType))
                {
                    errors.Add(new TypeChainError(
                        comp.Name,
                        i,
                        cur.ToComponent, cur.ToMethod, curRetType,
                        next.ToComponent, next.ToMethod, nextParamType));
                }
            }
        }

        return errors;
    }

    private static string? GetDeclaredReturnType(ConnectionSpec conn, ArchitectureContract contract)
    {
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp == null) return null;
        var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
        if (ms == null) return null;
        return ms.ReturnType;
    }

    private static string? GetDeclaredFirstParamType(ConnectionSpec conn, ArchitectureContract contract)
    {
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp == null) return null;
        var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
        if (ms == null || ms.Params.Length == 0) return null;
        return ms.Params[0].Type;
    }

    private static string? GetReturnType(ConnectionSpec conn, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> sigs)
    {
        if (sigs.TryGetValue(conn.ToComponent, out var methods))
        {
            var m = methods.FirstOrDefault(x => x.Name == conn.ToMethod)
                     ?? methods.FirstOrDefault(x => x.Name.Contains(conn.ToMethod) || conn.ToMethod.Contains(x.Name));
            if (m != null)
            {
                if (m.ReturnType == "void" && m.OutParamTypes.Length > 0)
                    return m.OutParamTypes[0];
                return m.ReturnType;
            }
        }
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp != null)
        {
            var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
            if (ms != null) return ms.ReturnType;
        }
        return null;
    }

    private static string? GetFirstParamType(ConnectionSpec conn, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> sigs)
    {
        if (sigs.TryGetValue(conn.ToComponent, out var methods))
        {
            var m = methods.FirstOrDefault(x => x.Name == conn.ToMethod)
                     ?? methods.FirstOrDefault(x => x.Name.Contains(conn.ToMethod) || conn.ToMethod.Contains(x.Name));
            if (m != null && m.ParamTypes.Length > 0) return m.ParamTypes[0];
        }
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp != null)
        {
            var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
            if (ms != null && ms.Params.Length > 0) return ms.Params[0].Type;
        }
        return null;
    }

    /// <summary>
    /// Native C# type compatibility check. Both sides use C# notation
    /// (string, int, bool, string[], List&lt;string&gt;, etc.).
    /// </summary>
    private static bool AreCompatible(string from, string to)
    {
        var f = from.Trim();
        var t = to.Trim();
        if (f == t) return true;
        // string ↔ int/double — model handles parse/format in wiring
        if (f == "string" && (t == "int" || t == "double" || t == "long")) return true;
        if (t == "string" && (f == "int" || f == "double" || f == "long")) return true;
        // string[] ↔ List<string> — interchangeable
        if (f == "string[]" && t == "List<string>") return true;
        if (t == "string[]" && f == "List<string>") return true;
        // string ↔ string[] — split/join at boundary
        if (f == "string" && t == "string[]") return true;
        if (t == "string" && f == "string[]") return true;
        // string ↔ List<string> — split/join
        if (f == "string" && t == "List<string>") return true;
        if (t == "string" && f == "List<string>") return true;
        // bool is terminal — doesn't chain to anything
        if (f == "bool") return false;
        if (t == "bool") return true; // anything can be checked as bool
        return false;
    }

    public static string FormatErrors(List<TypeChainError> errors)
    {
        if (errors.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"TypeChainChecker found {errors.Count} error(s):");
        foreach (var e in errors)
        {
            sb.AppendLine($"  [{e.Component}] step {e.StepIndex}: " +
                $"{e.FromComponent}.{e.FromMethod} returns '{e.FromType}' " +
                $"but {e.ToComponent}.{e.ToMethod} expects '{e.ToType}'");
            var fromIsArray = e.FromType.Contains("[]") || e.FromType.Contains("List<");
            var toIsArray = e.ToType.Contains("[]") || e.ToType.Contains("List<");
            var fromIsString = e.FromType == "string";
            var toIsString = e.ToType == "string";
            if (fromIsArray && toIsString)
                sb.AppendLine("    FIX: The last logic component must return 'string' (serialize its output), OR add a serialization step before the printer.");
            else if (fromIsString && toIsArray)
                sb.AppendLine("    FIX: Use a stub that returns the right type — ReadLines returns string[], ReadFile returns string. Match the stub to what the next component expects.");
            else
                sb.AppendLine("    FIX: Change the return type or input type so they match, OR add a conversion step between them.");
        }
        return sb.ToString();
    }
}

public record TypeChainError(
    string Component,
    int StepIndex,
    string FromComponent,
    string FromMethod,
    string FromType,
    string ToComponent,
    string ToMethod,
    string ToType);