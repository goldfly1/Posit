namespace Posit.Phases;

/// <summary>
/// A single scan error found when validating ArchitectureContract against the pattern registry.
/// </summary>
public record ScanError(
    string Component,
    string Field,
    string Value,
    string Message,
    string[] Available)
{
    public string Format() =>
        $"  scan.{Field}: component '{Component}' — {Value} {Message}" +
        (Available.Length > 0 ? $"\n  Available: {string.Join(", ", Available)}" : "");
}

/// <summary>
/// Scans ArchitectureContract against the pattern registry BEFORE the pipeline proceeds.
/// Checks every toMethod, fromMethod, patternName, stubName, dependency.
/// If any name doesn't match the registry, returns a ScanError listing what's available.
/// The correction listing is fed back to the model via CorrectionSignal.
/// </summary>
public static class ContractScanner
{
    /// <summary>
    /// Scan the contract. Returns list of errors (empty = clean).
    /// </summary>
    public static List<ScanError> Scan(ArchitectureContract contract, PatternRegistry registry)
    {
        var errors = new List<ScanError>();
        var componentNames = contract.Components.Select(c => c.Name).ToHashSet();

        foreach (var comp in contract.Components)
        {
            // Check patternName for dafny/mixed modules.
            // Components WITH connections don't need a patternName — the connections
            // ARE the spec, the wiring IS the implementation. No dead pattern body.
            if (comp.Classification != ModuleClassification.IoShell && comp.Connections.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(comp.PatternName))
                {
                    errors.Add(new ScanError(comp.Name, "patternName", "(empty)",
                        "is empty but component is classified as " + comp.Classification, []));
                }
                else if (!registry.HasPattern(comp.PatternName!))
                {
                    errors.Add(new ScanError(comp.Name, "patternName", comp.PatternName!,
                        "does not exist in the pattern registry",
                        registry.GetAllPatterns().Select(p => p.Name).ToArray()));
                }
                else
                {
                    // For cut-outs: check the model's declared method signatures match
                    // the REAL method names from the Dafny source. The model must use
                    // the real names, not invented ones.
                    // EXCEPTION: error-path methods (WriteError, PrintError) are custom
                    // additions by the architect for error handling — they won't be on
                    // the cut-out. Allow them when branching is present.
                    var realSigs = registry.GetMethodSignatures(comp.PatternName!);
                    if (realSigs.Count > 0)
                    {
                        var realMethodNames = realSigs.Select(s => s.Name).ToHashSet();
                        // Check if this component has branching
                        var hasBranching = !string.IsNullOrWhiteSpace(comp.BranchCondition)
                            || comp.MethodSignatures.Any(m =>
                                (m.ReturnDafnyType ?? m.ReturnType ?? "").Equals("bool", StringComparison.OrdinalIgnoreCase));
                        foreach (var ms in comp.MethodSignatures)
                        {
                            // Skip error-path methods when branching is present
                            if (hasBranching && IsErrorPathMethod(ms.Name))
                                continue;
                            if (!realMethodNames.Contains(ms.Name))
                            {
                                errors.Add(new ScanError(comp.Name, "methodSignature.name",
                                    ms.Name,
                                    $"does not exist on cut-out '{comp.PatternName}' (real methods: {string.Join(", ", realMethodNames)}). " +
                                    $"If this component needs methods the cut-out doesn't have, set patternName to null and write custom Dafny. " +
                                    $"Do NOT invent method names — use ONLY the real methods listed, or drop the cut-out.",
                                    [.. realMethodNames]));
                            }
                        }
                    }
                }
            }

            // io-shell components MUST have at least one stub
            if (comp.Classification == ModuleClassification.IoShell && comp.StubNames.Length == 0)
            {
                errors.Add(new ScanError(comp.Name, "stubNames", "(empty)",
                    "is empty but component is classified as io-shell — needs at least one stub",
                    registry.GetAllCSharpStubs().Select(s => s.Name).ToArray()));
            }

            // Check stubNames against C# stubs
            foreach (var stub in comp.StubNames)
            {
                if (!registry.HasCSharpStub(stub))
                    errors.Add(new ScanError(comp.Name, "stubName", stub,
                        "does not exist in the C# stub templates",
                        registry.GetAllCSharpStubs().Select(s => s.Name).ToArray()));
            }

            // Check dependencies resolve to real components
            foreach (var dep in comp.Dependencies)
            {
                if (!componentNames.Contains(dep))
                    errors.Add(new ScanError(comp.Name, "dependency", dep,
                        "does not resolve to any component in the contract", [.. componentNames]));
            }

            // Check connection toMethod against target component's methodSignatures
            foreach (var conn in comp.Connections)
            {
                var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
                if (targetComp == null)
                {
                    errors.Add(new ScanError(comp.Name, "connection.toComponent",
                        conn.ToComponent, "does not exist in the contract", [.. componentNames]));
                    continue;
                }

                var targetMethods = targetComp.MethodSignatures.Select(m => m.Name).ToHashSet();
                if (!targetMethods.Contains(conn.ToMethod))
                {
                    errors.Add(new ScanError(comp.Name, "connection.toMethod",
                        conn.ToMethod,
                        $"does not exist on target '{conn.ToComponent}'", [.. targetMethods]));
                }
            }

            // Check fromMethod exists on this component's own signatures
            var ownMethods = comp.MethodSignatures.Select(m => m.Name).ToHashSet();
            foreach (var conn in comp.Connections)
            {
                if (!ownMethods.Contains(conn.FromMethod))
                    errors.Add(new ScanError(comp.Name, "connection.fromMethod",
                        conn.FromMethod, "does not exist on this component's methodSignatures",
                        [.. ownMethods]));
            }

            // Declaration/use consistency: every declared method should be used in a
            // connection. Skip for connection-bearing components (orchestrators) —
            // their wiring calls other components, not their own methods.
            // EXCEPTION: when the component has a BranchCondition (error branching),
            // allow error-path methods (WriteError, PrintError, etc.) to be declared
            // but unused — they're called in the error branch, not the main chain.
            if (comp.Connections.Length == 0)
            {
                var allCalledMethods = new HashSet<string>();
                // Methods called ON this component (via other components' connections)
                foreach (var other in contract.Components)
                    foreach (var conn in other.Connections)
                        if (conn.ToComponent == comp.Name)
                            allCalledMethods.Add(conn.ToMethod);
                // Methods called FROM this component
                foreach (var conn in comp.Connections)
                    allCalledMethods.Add(conn.FromMethod);

                // Determine if this component has branching (BranchCondition set, or
                // any method signature returns bool — isValid pattern)
                var hasBranching = !string.IsNullOrWhiteSpace(comp.BranchCondition)
                    || comp.MethodSignatures.Any(m =>
                        (m.ReturnDafnyType ?? m.ReturnType ?? "").Equals("bool", StringComparison.OrdinalIgnoreCase));

                foreach (var declared in ownMethods)
                {
                    if (!allCalledMethods.Contains(declared))
                    {
                        // Allow error-path methods when branching is present
                        if (hasBranching && IsErrorPathMethod(declared))
                            continue;
                        errors.Add(new ScanError(comp.Name, "methodSignature.name",
                            declared, "is declared but never used in any connection",
                            [.. allCalledMethods]));
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Format the correction listing for the model via CorrectionSignal.
    /// </summary>
    public static string FormatCorrectionListing(List<ScanError> errors)
    {
        if (errors.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"ContractScanner found {errors.Count} error(s):");
        foreach (var err in errors)
            sb.AppendLine(err.Format());
        return sb.ToString();
    }

    /// <summary>
    /// Heuristic: does this method name look like an error-path method?
    /// Error-path methods are called in the branch (if !isValid), not the main chain.
    /// They don't need to appear in connections.
    /// </summary>
    private static bool IsErrorPathMethod(string methodName) =>
        methodName.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("WriteStderr", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("PrintError", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("Fail", StringComparison.OrdinalIgnoreCase);
}