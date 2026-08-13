using Posit.Contracts.Artifacts;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Scans an ArchitectureContract against the pattern registry to catch
/// hallucinated names BEFORE the pipeline wastes time translating and building.
///
/// This is the carapace enforcing at the design boundary — the "reject it" half.
/// The scanner reads the pattern files (the authority at design time) and checks
/// every name the architect invented against what actually exists.
///
/// Two scans, two layers, same principle:
///   - Validation pass (this class): scans pattern files, rejects bad names up front
///   - Wiring pass (TranslatedCSharpScanner): scans translated C#, wires against reality
///
/// The correction listing is fed back to the model via CorrectionSignal so it can
/// fix the names on retry. The loop continues until the contract is clean.
/// </summary>
public sealed class ContractScanner
{
    private readonly PatternRegistry _registry;

    public ContractScanner(PatternRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// A single validation finding from the contract scan.
    /// </summary>
    public record ScanError(
        string Component,    // which component has the problem
        string Field,         // which field is wrong (toMethod, fromMethod, patternName, etc.)
        string Value,         // the hallucinated value
        string Message,       // human-readable explanation
        string[] Available);  // what's actually available (for the model to pick from)

    /// <summary>
    /// Scan the entire architecture contract against the pattern registry.
    /// Returns a list of errors. Empty list = contract is clean.
    /// </summary>
    public List<ScanError> Scan(ArchitectureContract contract)
    {
        var errors = new List<ScanError>();

        var componentByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contract.Components)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                componentByName[c.Name] = c;
        }

        // Cache pattern signatures so we don't re-extract for every connection
        var patternSigCache = new Dictionary<string, List<MethodSignature>>(StringComparer.OrdinalIgnoreCase);

        List<MethodSignature> GetPatternSigsCached(string patternName)
        {
            if (patternSigCache.TryGetValue(patternName, out var cached))
                return cached;
            var sigs = _registry.HasPattern(patternName)
                ? _registry.GetPatternSignatures(patternName)
                : new List<MethodSignature>();
            patternSigCache[patternName] = sigs;
            return sigs;
        }

        foreach (var comp in contract.Components)
        {
            var compName = comp.Name ?? "(unnamed)";
            // ── 1. Pattern name validation ──
            // Dafny/mixed components must reference a real pattern.
            if (comp.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed)
            {
                if (string.IsNullOrWhiteSpace(comp.PatternName))
                {
                    errors.Add(new ScanError(
                        compName, "patternName", "(empty)",
                        "dafny/mixed component requires a patternName",
                        GetAvailablePatterns()));
                }
                else if (!_registry.HasPattern(comp.PatternName!))
                {
                    errors.Add(new ScanError(
                        compName, "patternName", comp.PatternName,
                        $"patternName '{comp.PatternName}' does not exist in the registry",
                        GetAvailablePatterns()));
                }
            }

            // ── 2. Stub name validation ──
            // Io-shell components (and dafny components with stubs) must reference real stubs.
            if (comp.StubNames is { Length: > 0 })
            {
                foreach (var stub in comp.StubNames)
                {
                    if (!_registry.HasCSharpStub(stub!))
                    {
                        errors.Add(new ScanError(
                            compName, "stubName", stub,
                            $"stubName '{stub}' does not exist in the C# stub registry",
                            GetAvailableCSharpStubs()));
                    }
                }
            }

            // ── 3. Dependency validation ──
            // Every dependency must reference a real component in this contract.
            if (comp.Dependencies is not null)
            {
                foreach (var dep in comp.Dependencies)
                {
                    if (!componentByName.ContainsKey(dep))
                    {
                        errors.Add(new ScanError(
                            compName, "dependency", dep,
                            $"dependency '{dep}' does not match any component in this contract",
                            componentByName.Keys.ToArray()));
                    }
                }
            }

            // ── 4. Connection validation ──
            if (comp.Connections is null || comp.Connections.Length == 0)
                continue;

            // Gather the source component's available method names (from MethodSignatures + publicSurface)
            var sourceMethodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (comp.MethodSignatures is not null)
            {
                foreach (var ms in comp.MethodSignatures)
                    sourceMethodNames.Add(ms.Name);
            }
            if (comp.PublicSurface is not null)
            {
                foreach (var ps in comp.PublicSurface)
                    sourceMethodNames.Add(ps);
            }

            foreach (var conn in comp.Connections)
            {
                // 4a. fromMethod must exist on THIS component
                if (!string.IsNullOrWhiteSpace(conn.FromMethod) &&
                    !sourceMethodNames.Contains(conn.FromMethod))
                {
                    errors.Add(new ScanError(
                        compName, "fromMethod", conn.FromMethod,
                        $"connection fromMethod '{conn.FromMethod}' does not match any method on '{compName}'",
                        sourceMethodNames.ToArray()));
                }

                // 4b. toComponent must reference a real component
                if (!string.IsNullOrWhiteSpace(conn.ToComponent) &&
                    !componentByName.ContainsKey(conn.ToComponent))
                {
                    errors.Add(new ScanError(
                        compName, "toComponent", conn.ToComponent,
                        $"connection toComponent '{conn.ToComponent}' does not match any component in this contract",
                        componentByName.Keys.ToArray()));
                    continue; // can't check toMethod if target doesn't exist
                }

                // 4c. toMethod must exist on the TARGET component's pattern
                if (string.IsNullOrWhiteSpace(conn.ToMethod))
                    continue;

                if (!componentByName.TryGetValue(conn.ToComponent, out var targetComp))
                    continue;

                var targetMethodNames = GetAvailableMethodNames(targetComp, GetPatternSigsCached);
                if (targetMethodNames.Count == 0)
                    continue; // can't validate if we don't know the target's methods

                if (!targetMethodNames.Contains(conn.ToMethod))
                {
                    errors.Add(new ScanError(
                        compName, "toMethod", conn.ToMethod,
                        $"connection toMethod '{conn.ToMethod}' does not exist on target '{conn.ToComponent}'" +
                        (string.IsNullOrWhiteSpace(targetComp.PatternName)
                            ? ""
                            : $" (pattern '{targetComp.PatternName}')"),
                        targetMethodNames.ToArray()));
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Get all method names available on a target component — from its MethodSignatures
    /// (architect's declared names + PatternMethod mappings) and from the pattern registry
    /// (the real method names on the pattern). The union of both, because either is a
    /// valid wiring target.
    /// </summary>
    private HashSet<string> GetAvailableMethodNames(
        Component targetComp,
        Func<string, List<MethodSignature>> getPatternSigsCached)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Architect's declared method signatures
        if (targetComp.MethodSignatures is not null)
        {
            foreach (var ms in targetComp.MethodSignatures)
            {
                names.Add(ms.Name);
                if (!string.IsNullOrWhiteSpace(ms.PatternMethod))
                    names.Add(ms.PatternMethod);
            }
        }

        // Pattern registry's real method names
        if (!string.IsNullOrWhiteSpace(targetComp.PatternName) &&
            _registry.HasPattern(targetComp.PatternName))
        {
            var patternSigs = getPatternSigsCached(targetComp.PatternName);
            foreach (var sig in patternSigs)
                names.Add(sig.Name);
        }

        // Io-shell public surface
        if (targetComp.PublicSurface is not null)
        {
            foreach (var ps in targetComp.PublicSurface)
                names.Add(ps);
        }

        return names;
    }

    private string[] GetAvailablePatterns()
    {
        // PatternRegistry doesn't expose a list directly, but we can get it from
        // the CSharpStubs dictionary keys... actually patterns are in _patterns.
        // We need to get pattern names. Use reflection or add a method.
        // For now, use the known patterns list.
        return new[]
        {
            "pipeline", "repository", "state-machine", "aggregator", "builder",
            "iterator", "result", "observer", "strategy", "graph", "cache",
            "scheduler", "reducer", "adapter", "filter", "parser", "validator",
            "transformer"
        };
    }

    private string[] GetAvailableCSharpStubs()
    {
        return _registry.CSharpStubs.Keys.ToArray();
    }

    /// <summary>
    /// Format scan errors as a correction listing for the model.
    /// This is the "send it back with a listing" message.
    /// </summary>
    public static string FormatCorrectionListing(List<ScanError> errors)
    {
        if (errors.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("CONTRACT VALIDATION FAILED — the following names in your architecture contract do not match anything in the pattern registry.");
        sb.AppendLine("Fix these before resubmitting:");
        sb.AppendLine();

        foreach (var err in errors)
        {
            sb.AppendLine($"Component '{err.Component}': {err.Message}");
            if (err.Available is { Length: > 0 })
                sb.AppendLine($"  Available {err.Field}s: {string.Join(", ", err.Available)}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}