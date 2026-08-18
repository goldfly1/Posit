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
    /// Uses scanned C# signatures when available (post-Dafny).
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

    /// <summary>
    /// Pre-Dafny type chain check. Runs AFTER architecture, BEFORE Dafny —
    /// the data flow spec check. Uses declared method signatures from the
    /// contract (returnDafnyType / DafnyType) instead of scanned C# types.
    /// This catches type mismatches before anyone writes code, so the
    /// correction signal routes back to the architect, not the Dafny writer.
    /// </summary>
    public static List<TypeChainError> CheckPreDafny(ArchitectureContract contract)
    {
        var errors = new List<TypeChainError>();

        foreach (var comp in contract.Components)
        {
            if (comp.Connections.Length < 2) continue;

            for (int i = 0; i < comp.Connections.Length - 1; i++)
            {
                var cur = comp.Connections[i];
                var next = comp.Connections[i + 1];

                // Get the return type from the contract's declared method signatures
                var curRetType = GetDeclaredReturnType(cur, contract);
                if (curRetType == null) continue;
                if (curRetType == "void" || curRetType == "Void") continue;

                var nextParamType = GetDeclaredFirstParamType(next, contract);
                if (nextParamType == null) continue;

                // For pre-Dafny, we compare Dafny types (returnDafnyType, DafnyType)
                // These use Dafny notation: seq<string>, string, int, bool, etc.
                if (!AreCompatibleDafny(curRetType, nextParamType))
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
    /// Get the return type from the contract's declared method signatures.
    /// Uses ReturnDafnyType (Dafny notation) if available, falls back to ReturnType.
    /// Handles void+out params: first out param is the data return.
    /// </summary>
    private static string? GetDeclaredReturnType(ConnectionSpec conn, ArchitectureContract contract)
    {
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp == null) return null;
        var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
        if (ms == null) return null;
        // If returnType is void but there are out params (Dafny multi-return),
        // the first param with DafnyType containing "out" or just the first param
        // is the data return. But in the contract, out params aren't marked.
        // Use ReturnDafnyType if available, otherwise ReturnType.
        var ret = !string.IsNullOrWhiteSpace(ms.ReturnDafnyType) ? ms.ReturnDafnyType : ms.ReturnType;
        // If void, check if any param has a DafnyType that looks like a bool (isValid)
        // — the first non-bool param is the data return
        if (ret == "void" || ret == "Void")
        {
            // For Dafny multi-return, the contract's returnType might be void
            // but the method actually returns multiple values. In Dafny notation,
            // the return type would be something like "(seq<seq<string>>, bool)".
            // If ReturnDafnyType captures this, use it. Otherwise skip.
            if (!string.IsNullOrWhiteSpace(ms.ReturnDafnyType) && ms.ReturnDafnyType != "void")
                return ms.ReturnDafnyType;
            return "void"; // can't determine — skip
        }
        return ret;
    }

    /// <summary>
    /// Get the first param type from the contract's declared method signatures.
    /// Uses DafnyType if available, falls back to Type.
    /// </summary>
    private static string? GetDeclaredFirstParamType(ConnectionSpec conn, ArchitectureContract contract)
    {
        var comp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
        if (comp == null) return null;
        var ms = comp.MethodSignatures.FirstOrDefault(x => x.Name == conn.ToMethod);
        if (ms == null || ms.Params.Length == 0) return null;
        var p = ms.Params[0];
        return !string.IsNullOrWhiteSpace(p.DafnyType) ? p.DafnyType : p.Type;
    }

    /// <summary>
    /// Dafny-type compatibility check. Compares Dafny notation types
    /// (seq&lt;string&gt;, string, int, bool, etc.) without C# runtime types.
    /// </summary>
    private static bool AreCompatibleDafny(string from, string to)
    {
        var f = from.Trim();
        var t = to.Trim();
        if (f == t) return true;
        // string ↔ seq<string> — join/split at boundary (1D is OK)
        if (f == "string" && t == "seq<string>") return true;
        if (t == "string" && f == "seq<string>") return true;
        // seq<seq<string>> → string is OK (2D to string for printing)
        if (t == "string" && f == "seq<seq<string>>") return true;
        // string → seq<seq<string>> is a DIMENSIONAL MISMATCH — not compatible.
        // Use ReadLines (returns seq<string>) instead of ReadFile (returns string).
        // This is the exact bug we want to catch before code is written.
        // int ↔ BigInteger — same in Dafny
        if ((f == "int" || f == "BigInteger") && (t == "int" || t == "BigInteger")) return true;
        // string ↔ int — model handles parse/format
        if (f == "string" && t == "int") return true;
        if (t == "string" && f == "int") return true;
        // bool is terminal — doesn't chain to anything
        if (f == "bool") return false;
        if (t == "bool") return true; // anything can be checked as bool
        // seq<seq<string>> ↔ seq<string> — dimensional mismatch, NOT compatible
        // (this is the error we want to catch early!)
        // Same nesting depth = compatible
        var fDepth = CountDafnySeqDepth(f);
        var tDepth = CountDafnySeqDepth(t);
        if (fDepth > 0 && fDepth == tDepth) return true;
        // Different depth with seq types = incompatible (catches the ReadFile vs ReadLines bug)
        if (fDepth != tDepth && (fDepth > 0 || tDepth > 0)) return false;
        return false;
    }

    /// <summary>Count seq nesting depth in Dafny notation (seq&lt;string&gt; = 1, seq&lt;seq&lt;string&gt;&gt; = 2).</summary>
    private static int CountDafnySeqDepth(string type)
    {
        int count = 0, idx = 0;
        while ((idx = type.IndexOf("seq<", idx)) >= 0) { count++; idx += 4; }
        return count;
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
        // string ↔ BigInteger/int — model handles parsing/formatting in wiring
        if (f == "string" && (t == "BigInteger" || t == "int")) return true;
        if (t == "string" && (f == "BigInteger" || f == "int")) return true;
        // string ↔ ISequence<Rune> (1D)
        if (f == "string" && tDepth == 1 && tHasRune) return true;
        if (t == "string" && fDepth == 1 && fHasRune) return true;
        // seq<string> (Dafny notation) ↔ ISequence<Rune> (1D) — join lines into one string
        if (f == "seq<string>" && tDepth == 1 && tHasRune) return true;
        if (t == "seq<string>" && fDepth == 1 && fHasRune) return true;
        // string[] ↔ ISequence<Rune> (1D) — join lines into one string
        if (f == "string[]" && tDepth == 1 && tHasRune) return true;
        if (t == "string[]" && fDepth == 1 && fHasRune) return true;
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
            // Actionable guidance: tell the model HOW to fix it
            if (e.FromType.Contains("ISequence") && e.ToType == "string")
                sb.AppendLine("    FIX: The last logic component must return 'string' (serialize its output to a string), OR add a serialization step before the printer.");
            else if (e.FromType == "string" && e.ToType.Contains("ISequence"))
                sb.AppendLine("    FIX: Use a stub that returns the right type — ReadLines returns seq<string>, ReadFile returns string. Match the stub to what the next component expects.");
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