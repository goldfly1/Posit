namespace Posit.Phases;

/// <summary>
/// A single scan error found when validating ArchitectureContract.
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
/// Validates: C# interface structure, method coverage, stub names against registry,
/// dependency resolution, connection targets.
/// Registry stays as a reference for stub name validation.
/// </summary>
public static class ContractScanner
{
    public static List<ScanError> Scan(ArchitectureContract contract, PatternRegistry registry)
    {
        var errors = new List<ScanError>();
        var componentNames = contract.Components.Select(c => c.Name).ToHashSet();

        foreach (var comp in contract.Components)
        {
            // ── C# interface validation for logic components ──
            if (comp.Classification != ModuleClassification.IoShell)
            {
                if (!string.IsNullOrWhiteSpace(comp.CSharpInterface))
                    errors.AddRange(ValidateCSharpInterface(comp));
                else
                    errors.Add(new ScanError(comp.Name, "csharpInterface", "(missing)",
                        "is null — the architect must provide a C# interface for logic components",
                        []));
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

            // Check fromMethod exists on this component's own signatures OR is a stub method
            var ownMethods = comp.MethodSignatures.Select(m => m.Name).ToHashSet();
            var stubMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ReadLines", "ReadFile", "PrintLine", "PrintLines", "PrintError",
                "WriteFile", "AppendFile", "ReadStdin", "WriteStdout", "WriteStderr",
                "ReadInt", "ReadBool"
            };
            foreach (var conn in comp.Connections)
            {
                if (!ownMethods.Contains(conn.FromMethod) && !stubMethods.Contains(conn.FromMethod))
                    errors.Add(new ScanError(comp.Name, "connection.fromMethod",
                        conn.FromMethod, "does not exist on this component's methodSignatures or known stub methods",
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
                        m.ReturnType.Equals("bool", StringComparison.OrdinalIgnoreCase));

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
    /// Validate the C# interface written by the architect.
    /// Checks: interface declaration matches component name, method coverage,
    /// no method bodies (interface methods are signatures only).
    /// </summary>
    private static List<ScanError> ValidateCSharpInterface(Component comp)
    {
        var errors = new List<ScanError>();
        var iface = comp.CSharpInterface!;

        // Check interface declaration matches component name
        if (!iface.Contains($"interface I{comp.Name}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ScanError(comp.Name, "csharpInterface", "(interface)",
                $"must contain 'interface I{comp.Name}' — interface name must match component name",
                []));
        }

        // Check every declared MethodSignature appears in the interface
        foreach (var ms in comp.MethodSignatures)
        {
            if (!iface.Contains(ms.Name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ScanError(comp.Name, "csharpInterface", ms.Name,
                    "declared in methodSignatures but not found in csharpInterface — every method signature must appear in the interface",
                    []));
            }
        }

        // Check for method bodies (interface should be signatures only)
        // A body is `{ <statements> }` after a method signature. Look for implementation indicators.
        var bodyIndicators = new[] { "var ", "while ", "for ", "return " };
        foreach (var indicator in bodyIndicators)
        {
            if (iface.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ScanError(comp.Name, "csharpInterface", indicator.Trim(),
                    $"found in interface — the interface must be SIGNATURES ONLY. " +
                    $"C#Impl fills in the bodies. Remove '{indicator.Trim()}' statements from the interface.",
                    []));
                break;
            }
        }

        return errors;
    }

    public static string FormatCorrectionListing(List<ScanError> errors)
    {
        if (errors.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"ContractScanner found {errors.Count} error(s):");
        foreach (var err in errors)
            sb.AppendLine(err.Format());
        return sb.ToString();
    }

    private static bool IsErrorPathMethod(string methodName) =>
        methodName.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("WriteStderr", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("PrintError", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("Fail", StringComparison.OrdinalIgnoreCase);
}