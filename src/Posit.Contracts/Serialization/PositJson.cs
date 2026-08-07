using System.Text.Json;
using System.Text.Json.Serialization;
using Posit.Contracts.Core;

namespace Posit.Contracts.Serialization;

/// <summary>
/// The canonical JSON serializer options for all model-facing contracts in Posit.
/// Centralized here so every phase, the orchestrator, and the dashboard use the
/// same tolerant, model-output-accepting settings — no per-file converter ordering.
/// </summary>
public static class PositJson
{
    /// <summary>
    /// Use this for every JSON serialize/deserialize of AI-generated artifacts.
    /// - camelCase properties, case-insensitive names
    /// - tolerant enum parsing (kebab-case, PascalCase, snake_case all accepted)
    /// - specialized ModuleClassification synonyms ("io-shell", "io_shell", "partial", "IO", etc.)
    /// - do NOT throw on missing members
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
        Converters =
        {
            new TolerantStringEnumConverter(),
            new ModuleClassificationConverter()
        }
    };
}
