namespace Posit.Phases;

/// <summary>
/// Phase E: contract-fidelity gate. Runs after ContractScanner, before Success.
/// Checks that the architect's contract actually COVERS the spec's intent —
/// not just that it's well-formed (ContractScanner's job) but that the spec's
/// action verbs have method/test-case anchors and the contract isn't degenerate
/// (1-component collapse of a multi-verb spec).
///
/// Kills three bugs at the root:
///   #6 architect nondeterminism (T8 a3: degenerate 1-component stub for a
///      parse→filter→count spec)
///   #7 model ceiling mitigation (reject contracts that can't express the spec's
///      output shape, forcing a re-roll instead of a wasted impl cycle)
///   #8 fixer oscillation (the fixer can't fix a contract that doesn't cover
///      the spec — reject early, don't burn retries)
/// </summary>
public static class ContractFidelityChecker
{
    // Action verbs the spec may use — matched case-insensitively as substrings
    // against the union of method names, test-case names, and test-case descriptions.
    // Extended as new trials surface new verbs.
    //
    // NOTE: "print", "read", "write" are I/O verbs — they describe the CLI
    // orchestrator's job (handled by EmitPrint / the entry harness), not logic
    // decomposition. Counting them as action verbs would force a degenerate
    // single-method spec like "convert and print" to decompose into two methods
    // when one (ConvertTemperature) is the correct shape (T6 rack evidence).
    // They remain in the COVERAGE check (a method/test mentioning them is fine)
    // but are EXCLUDED from the degenerate-contract verb count.
    private static readonly string[] ActionVerbs =
    [
        "filter", "count", "parse", "merge", "convert", "validate", "sort",
        "format", "transform", "export", "import", "analyze",
        "aggregate", "group", "search", "replace", "split", "detect", "check",
        "calculate", "compare", "extract", "load", "save",
    ];

    // I/O verbs excluded from the degenerate-contract verb count (they're the
    // orchestrator's responsibility, not logic decomposition).
    // "format" is included because it's frequently a noun ("in the format X")
    // rather than a verb ("format the output") — causes false positives.
    private static readonly string[] IoVerbs = ["print", "read", "write", "format"];

    /// <summary>
    /// Check contract fidelity against the original spec text.
    /// Returns a list of fidelity errors (empty = pass).
    /// </summary>
    public static List<FidelityError> Check(ArchitectureContract contract, string? specText)
    {
        var errors = new List<FidelityError>();
        if (string.IsNullOrWhiteSpace(specText))
            return errors; // No spec to check against — skip (e.g., smoke tests)

        // ── Check 1: Spec-verb coverage ──
        // Scan both action verbs AND I/O verbs for coverage, but only action
        // verbs count toward the degenerate-contract verb total (Check 2).
        var allScanVerbs = ActionVerbs.Concat(IoVerbs).ToArray();
        var specLower = specText.ToLowerInvariant();
        var foundVerbs = new HashSet<string>();
        var missingVerbs = new List<string>();

        foreach (var verb in allScanVerbs)
        {
            if (!specLower.Contains(verb))
                continue; // Spec doesn't use this verb — irrelevant

            // Does any method name, test-case name, or test-case description cover it?
            var covered = false;
            foreach (var comp in contract.Components)
            {
                foreach (var ms in comp.MethodSignatures)
                {
                    if (ms.Name.ToLowerInvariant().Contains(verb))
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered) break;

                if (comp.TestCases is { Length: > 0 })
                {
                    foreach (var tc in comp.TestCases)
                    {
                        if ((tc.Name ?? "").ToLowerInvariant().Contains(verb) ||
                            (tc.Description ?? "").ToLowerInvariant().Contains(verb) ||
                            (tc.ExpectedBehavior ?? "").ToLowerInvariant().Contains(verb))
                        {
                            covered = true;
                            break;
                        }
                    }
                }
                if (covered) break;
            }

            if (covered)
                foundVerbs.Add(verb);
            else
                missingVerbs.Add(verb);
        }

        if (missingVerbs.Count > 0 && foundVerbs.Count > 0)
        {
            // Only fail if there ARE found verbs (spec has action verbs) but some
            // are missing. A spec with zero action verbs (pure data description)
            // skips this check.
            var coverage = (double)foundVerbs.Count / (foundVerbs.Count + missingVerbs.Count);
            if (coverage < 0.5)
            {
                errors.Add(new FidelityError(
                    "spec-verb-coverage",
                    $"Spec uses action verbs [{string.Join(", ", missingVerbs)}] but no component method, "
                    + $"test-case name, or test-case description covers them. "
                    + $"Found: [{string.Join(", ", foundVerbs)}]. "
                    + $"The contract must decompose the spec's verbs into component methods."));
            }
        }

        // ── Check 2: Degenerate contract rejection ──
        // If the spec has ≥2 distinct ACTION verbs (I/O verbs excluded — "print"
        // is the orchestrator's job) but the contract has 1 logic component with
        // 1 method, the architect collapsed a multi-step spec.
        var foundActionVerbs = foundVerbs
            .Where(v => !IoVerbs.Contains(v))
            .ToList();
        var logicComponents = contract.Components
            .Where(c => c.Classification != ModuleClassification.IoShell)
            .ToList();
        var totalLogicMethods = logicComponents.Sum(c => c.MethodSignatures.Length);

        if (foundActionVerbs.Count >= 2 && logicComponents.Count == 1 && totalLogicMethods == 1)
        {
            var methodName = logicComponents[0].MethodSignatures[0]?.Name ?? "?";
            // Exempt combined methods: if the method name itself contains
            // multiple action verbs (e.g. ValidateAndMerge, ParseAndValidate),
            // it's an intentional combined method, not a degenerate collapse.
            // The bool-chain-mismatch check explicitly recommends this pattern.
            var methodLower = methodName.ToLowerInvariant();
            var verbsInName = foundActionVerbs.Count(v => methodLower.Contains(v));
            if (verbsInName < 2)
            {
                errors.Add(new FidelityError(
                    "degenerate-contract",
                    $"Spec has {foundActionVerbs.Count} action verbs ({string.Join(", ", foundActionVerbs)}) "
                    + $"but the contract has ONE logic component with ONE method "
                    + $"('{methodName}'). A multi-verb spec needs decomposition into "
                    + $"multiple methods or components — one method cannot cover "
                    + $"parse+filter+count (or equivalent). Re-decompose the spec."));
            }
        }

        // ── Check 3: Connection completeness ──
        // Every logic component must appear as a ToComponent in some connection.
        // T10 rack: architect declared a ProductCsvProcessor logic component but
        // connected CLI → FileIO.ReadFile → print, bypassing the logic entirely.
        // The program printed raw file content because no transformation ran.
        var connectedComponents = contract.Components
            .Where(c => c.Connections is { Length: > 0 })
            .SelectMany(c => c.Connections)
            .Select(conn => conn.ToComponent)
            .ToHashSet();
        foreach (var logic in logicComponents)
        {
            if (!connectedComponents.Contains(logic.Name))
            {
                errors.Add(new FidelityError(
                    "unconnected-logic",
                    $"Logic component '{logic.Name}' is declared but never called in any "
                    + $"connection. Every logic component must appear as a connection target "
                    + $"(ToComponent). The connection chain must include all logic components."));
            }
        }

        // ── Check 4: Bool-return type-chain mismatch ──
        // The #1 recurring architecture failure: Validate returns bool but the
        // next method in the chain expects data (string[], string, etc.).
        // The TypeChainChecker catches this too, but its error message is generic.
        // This check gives a SPECIFIC correction: "return DATA from validation,
        // not bool. Use Result<T> or combine validate+merge into one method."
        foreach (var comp in contract.Components)
        {
            if (comp.Connections is not { Length: > 1 }) continue;
            for (var i = 0; i < comp.Connections.Length - 1; i++)
            {
                var conn = comp.Connections[i];
                var targetComp = contract.Components.FirstOrDefault(c => c.Name == conn.ToComponent);
                if (targetComp == null) continue;
                var targetSig = targetComp.MethodSignatures
                    .FirstOrDefault(m => m.Name == conn.ToMethod);
                if (targetSig == null) continue;

                // Check if this method returns bool and the NEXT method's first
                // param expects a different type
                var retType = NormalizeReturnType(targetSig.ReturnType);
                if (retType != "bool") continue;

                var nextConn = comp.Connections[i + 1];
                var nextComp = contract.Components.FirstOrDefault(c => c.Name == nextConn.ToComponent);
                if (nextComp == null) continue;
                var nextSig = nextComp.MethodSignatures
                    .FirstOrDefault(m => m.Name == nextConn.ToMethod);
                if (nextSig == null) continue;

                if (nextSig.Params.Length > 0)
                {
                    var nextFirstType = nextSig.Params[0].Type;
                    // bool → string/string[]/int/double/etc. = mismatch
                    if (nextFirstType != "bool")
                    {
                        errors.Add(new FidelityError(
                            "bool-chain-mismatch",
                            $"Component '{conn.ToComponent}' method '{conn.ToMethod}' returns bool, "
                            + $"but the next step '{nextConn.ToComponent}.{nextConn.ToMethod}' expects "
                            + $"'{nextFirstType}'. bool CANNOT chain into data. "
                            + $"FIX: Either (a) return the validated DATA (e.g. string[] or Result<T>) "
                            + $"from the validation method so the chain continues, or (b) combine "
                            + $"validation and the next step into ONE method that returns the result. "
                            + $"Do NOT use bool as a return type in a data pipeline chain."));
                    }
                }
            }
        }

        // ── Check 5: Output-shape mismatch ──
        // If the spec demands formatted output (contains a print-format hint like
        // 'LEVEL: N' or 'key=value') but no test case carries OutputFormat, warn.
        // This is a soft check (warning, not hard fail) — the architect might
        // return a pre-formatted string from the logic itself.
        // TODO: enable when OutputFormat is proven on more trials.

        return errors;
    }

    // ── Helpers ──

    private static string NormalizeReturnType(string type)
    {
        if (string.IsNullOrEmpty(type)) return type;
        var t = type.Trim().Replace(" ", "");
        if (t.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            t = t[(t.LastIndexOf('.') + 1)..];
        if (t.Equals("Boolean", StringComparison.OrdinalIgnoreCase)) return "bool";
        if (t.Equals("Int32", StringComparison.OrdinalIgnoreCase)) return "int";
        if (t.Equals("Int64", StringComparison.OrdinalIgnoreCase)) return "long";
        if (t.Equals("Double", StringComparison.OrdinalIgnoreCase)) return "double";
        if (t.Equals("String", StringComparison.OrdinalIgnoreCase)) return "string";
        return t.ToLowerInvariant();
    }

    public static string FormatErrors(List<FidelityError> errors) =>
        string.Join("\n", errors.Select(e => $"[fidelity:{e.Rule}] {e.Message}"));
}

public record FidelityError(string Rule, string Message);