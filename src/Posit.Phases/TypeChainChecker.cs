namespace Posit.Phases;

/// <summary>
/// Post-Dafny type chain checker. Runs AFTER Dafny Implementation (where real
/// C# types exist) and BEFORE C# Implementation. For each consecutive pair of
/// connections on a component, checks that the return type of step N is
/// compatible with the first param of step N+1. If not, returns errors that
/// route back to Architecture via CorrectionSignal.
/// </summary>
public static class TypeChainChecker
{
    /// <summary>
    /// Check the type chain. Returns list of errors (empty = clean).
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

                // Get the return type of the current step's target method
                var curRetType = GetReturnType(cur, contract, scannedSigs);
                if (curRetType == null) continue;

                // Get the first param type of the next step's target method
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

    private static string? GetReturnType(ConnectionSpec conn, ArchitectureContract contract,
        Dictionary<string, List<CsMethodSignature>> sigs)
    {
        // Look up the target component's scanned method signatures
        if (sigs.TryGetValue(conn.ToComponent, out var methods))
        {
            var m = methods.FirstOrDefault(x => x.Name == conn.ToMethod)
                     ?? methods.FirstOrDefault(x => x.Name.Contains(conn.ToMethod) || conn.ToMethod.Contains(x.Name));
            if (m != null) return m.ReturnType;
        }
        // Fall back to contract's declared method signatures
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
    /// Two types are compatible if they're equal, or ConvertType handles them.
    /// string ↔ ISequence&lt;Rune&gt;: convertible. Everything else must match.
    /// </summary>
    private static bool AreCompatible(string from, string to)
    {
        if (from == to) return true;
        // string ↔ ISequence<Rune> (but NOT dimensionality upgrades)
        if (from == "string" && to.Contains("ISequence") && to.Contains("Rune")
            && to.IndexOf("ISequence", 1) < 0)
            return true;
        if (to == "string" && from.Contains("ISequence") && from.Contains("Rune")
            && from.IndexOf("ISequence", 1) < 0)
            return true;
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