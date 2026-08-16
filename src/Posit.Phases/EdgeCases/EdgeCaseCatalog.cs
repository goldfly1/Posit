namespace Posit.Phases.EdgeCases;

/// <summary>
/// Catalog of edge case test patterns, selected based on module name and public surface.
/// </summary>
public static class EdgeCaseCatalog
{
    /// <summary>
    /// Returns the edge case patterns applicable to a given module based on its name
    /// and the public API surface it exposes.
    /// </summary>
    /// <param name="moduleName">The name of the module (e.g. "UserApi", "OrderRepository").</param>
    /// <param name="publicSurface">An array of public surface identifiers exposed by the module
    /// (e.g. "Store", "Async", "Query").</param>
    /// <returns>A list of <see cref="EdgeCasePattern"/> entries appropriate for the module.</returns>
    public static List<EdgeCasePattern> GetPatternsForModule(string moduleName, string[] publicSurface)
    {
        var patterns = new List<EdgeCasePattern>(InputValidationPatterns.All);

        var surface = publicSurface ?? [];
        foreach (var surfaceName in surface)
        {
            if (surfaceName is null)
            {
                continue;
            }

            if (surfaceName.Contains("Store", StringComparison.Ordinal)
                || surfaceName.Contains("Repository", StringComparison.Ordinal)
                || surfaceName.Contains("Query", StringComparison.Ordinal))
            {
                patterns.AddRange(SqlInjectionPatterns.All);
                break;
            }
        }

        foreach (var surfaceName in surface)
        {
            if (surfaceName is null)
            {
                continue;
            }

            if (surfaceName.Contains("Async", StringComparison.Ordinal)
                || surfaceName.Contains("Task", StringComparison.Ordinal))
            {
                patterns.AddRange(ConcurrencyPatterns.All);
                break;
            }
        }

        if (moduleName is not null
            && (moduleName.Contains("Api", StringComparison.Ordinal)
                || moduleName.Contains("Controller", StringComparison.Ordinal)
                || moduleName.Contains("Endpoint", StringComparison.Ordinal)))
        {
            patterns.AddRange(ApiErrorPatterns.All);
        }

        return patterns;
    }
}

/// <summary>
/// A single edge case pattern with category, name, description, and test guidance.
/// </summary>
public record EdgeCasePattern(string Category, string Name, string Description, string TestGuidance);