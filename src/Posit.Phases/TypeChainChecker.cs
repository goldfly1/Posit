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
                // Skip void returns — terminal actions (Print, WriteStdout) don't chain
                if (curRetType == "void" || curRetType == "Void") continue;

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
            if (m != null)
            {
                // Dafny multi-return translates to void + out params.
                // The first out param is the "data return" (e.g. outRows).
                if (m.ReturnType == "void" && m.OutParamTypes.Length > 0)
                    return m.OutParamTypes[0];
                return m.ReturnType;
            }
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
    /// Mirrors WiringGenerator.ConvertType's conversion table.
    /// </summary>
    private static bool AreCompatible(string from, string to)
    {
        // Normalize: strip "Dafny." prefixes for comparison
        var f = from.Replace("Dafny.", "");
        var t = to.Replace("Dafny.", "");
        if (f == t) return true;
        // Count ISequence nesting depth
        int fDepth = CountOccurrences(f, "ISequence");
        int tDepth = CountOccurrences(t, "ISequence");
        bool fHasRune = f.Contains("Rune");
        bool tHasRune = t.Contains("Rune");
        // string ↔ ISequence<Rune> (1D)
        if (f == "string" && tDepth == 1 && tHasRune) return true;
        if (t == "string" && fDepth == 1 && fHasRune) return true;
        // string[] ↔ ISequence<ISequence<Rune>> (2D — array to seq of seqs)
        if (f == "string[]" && tDepth == 2 && tHasRune) return true;
        if (t == "string[]" && fDepth == 2 && fHasRune) return true;
        // seq<string> (Dafny notation) ↔ ISequence<ISequence<Rune>> (C# notation)
        if (f == "seq<string>" && tDepth == 2 && tHasRune) return true;
        if (t == "seq<string>" && fDepth == 2 && fHasRune) return true;
        // seq<seq<string>> ↔ ISequence<ISequence<ISequence<Rune>>>
        if (f == "seq<seq<string>>" && tDepth == 3 && tHasRune) return true;
        if (t == "seq<seq<string>>" && fDepth == 3 && fHasRune) return true;
        // Same ISequence depth with Rune = compatible (same type, different notation)
        if (fDepth > 0 && fDepth == tDepth && fHasRune && tHasRune) return true;
        return false;
    }

    private static int CountOccurrences(string s, string sub)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf(sub, idx)) >= 0) { count++; idx += sub.Length; }
        return count;
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