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
/// Scans ArchitectureContract before the pipeline proceeds.
/// Validates: Dafny interface structure, method coverage, stub names against registry,
/// dependency resolution, connection targets.
/// Registry stays as a reference for name validation (stubs, types).
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
            // ── Dafny interface validation (new: replaces pattern method checks) ──
            if (comp.Classification != ModuleClassification.IoShell)
            {
                if (!string.IsNullOrWhiteSpace(comp.DafnyInterface))
                {
                    errors.AddRange(ValidateDafnyInterface(comp));
                }
                else if (string.IsNullOrWhiteSpace(comp.PatternName))
                {
                    // No interface AND no pattern — can't create skeleton
                    errors.Add(new ScanError(comp.Name, "dafnyInterface", "(missing)",
                        "is null and patternName is null — the architect must provide a Dafny interface for dafny components",
                        []));
                }
                // Legacy: pattern-based components still validated against registry
                else if (registry.HasPattern(comp.PatternName!))
                {
                    var realSigs = registry.GetMethodSignatures(comp.PatternName!);
                    if (realSigs.Count > 0)
                    {
                        var realMethodNames = realSigs.Select(s => s.Name).ToHashSet();
                        var hasBranching = !string.IsNullOrWhiteSpace(comp.BranchCondition)
                            || comp.MethodSignatures.Any(m =>
                                (m.ReturnDafnyType ?? m.ReturnType ?? "").Equals("bool", StringComparison.OrdinalIgnoreCase));
                        foreach (var ms in comp.MethodSignatures)
                        {
                            if (hasBranching && IsErrorPathMethod(ms.Name))
                                continue;
                            if (!realMethodNames.Contains(ms.Name))
                            {
                                errors.Add(new ScanError(comp.Name, "methodSignature.name",
                                    ms.Name,
                                    $"does not exist on cut-out '{comp.PatternName}' (real methods: {string.Join(", ", realMethodNames)}). " +
                                    $"If this component needs methods the cut-out doesn't have, set patternName to null and write a dafnyInterface. " +
                                    $"Do NOT invent method names — use ONLY the real methods listed, or drop the cut-out.",
                                    [.. realMethodNames]));
                            }
                        }
                    }
                }
                else
                {
                    errors.Add(new ScanError(comp.Name, "patternName", comp.PatternName!,
                        "does not exist in the pattern registry and no dafnyInterface provided",
                        registry.GetAllPatterns().Select(p => p.Name).ToArray()));
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
            // connection. Skip for connection-bearing components (orchestrators).
            if (comp.Connections.Length == 0)
            {
                var allCalledMethods = new HashSet<string>();
                foreach (var other in contract.Components)
                    foreach (var conn in other.Connections)
                        if (conn.ToComponent == comp.Name)
                            allCalledMethods.Add(conn.ToMethod);
                foreach (var conn in comp.Connections)
                    allCalledMethods.Add(conn.FromMethod);

                var hasBranching = !string.IsNullOrWhiteSpace(comp.BranchCondition)
                    || comp.MethodSignatures.Any(m =>
                        (m.ReturnDafnyType ?? m.ReturnType ?? "").Equals("bool", StringComparison.OrdinalIgnoreCase));

                foreach (var declared in ownMethods)
                {
                    if (!allCalledMethods.Contains(declared))
                    {
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
    /// Validate the Dafny interface written by the architect.
    /// Checks: module declaration matches component name, method coverage,
    /// no method bodies, {:extern} portal presence.
    /// </summary>
    private static List<ScanError> ValidateDafnyInterface(Component comp)
    {
        var errors = new List<ScanError>();
        var iface = comp.DafnyInterface!;

        // Check module declaration matches component name
        if (!iface.Contains($"module {comp.Name}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ScanError(comp.Name, "dafnyInterface", "(module)",
                $"must start with 'module {comp.Name} {{' — module name must match component name",
                []));
        }

        // Check every declared MethodSignature appears in the interface
        foreach (var ms in comp.MethodSignatures)
        {
            if (!iface.Contains($"method {ms.Name}", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ScanError(comp.Name, "dafnyInterface", ms.Name,
                    $"declared in methodSignatures but not found in dafnyInterface — every method signature must appear in the interface",
                    []));
            }
        }

        // Check for method bodies (interface should be bodyless — signatures + contracts only)
        // A body is `{ <statements> }` after a method signature. We look for var/:=/while/return
        // inside the interface, which indicates the architect wrote implementation, not just interface.
        var bodyIndicators = new[] { "var ", ":=", "while ", "return " };
        foreach (var indicator in bodyIndicators)
        {
            if (iface.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ScanError(comp.Name, "dafnyInterface", indicator.Trim(),
                    $"found in interface — the interface must be BODYLESS (signatures + contracts only). " +
                    $"DafnyImpl fills in the bodies. Remove '{indicator.Trim()}' statements from the interface.",
                    []));
                break; // one warning is enough
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