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
    /// - null arrays → empty arrays (model sends "connections": null)
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
            new ModuleClassificationConverter(),
            new NullToArrayConverter<ConnectionSpec>(),
            new NullToArrayConverter<MethodSignature>(),
            new NullToArrayConverter<string>(),
            new NullToArrayConverter<ComponentTestCase>(),
            new NullToArrayConverter<SharedTypeRef>(),
            new NullToArrayConverter<Component>()
        }
    };
}

/// <summary>
/// Converts JSON null to an empty array during deserialization.
/// Models frequently send "connections": null or "argMappings": null
/// instead of an empty array. This converter ensures [] is used instead.
/// </summary>
public sealed class NullToArrayConverter<T> : JsonConverter<T[]>
{
    public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        // Handle the case where the model sends array of objects instead of strings
        // e.g. argMappings: [{}] or argMappings: [{"source":"x","target":"y"}]
        if (typeof(T) == typeof(string) && reader.TokenType == JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var result = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                    result.Add(el.GetString() ?? "");
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    var source = el.TryGetProperty("source", out var s) ? s.GetString() : "";
                    var target = el.TryGetProperty("target", out var t) ? t.GetString() : "";
                    if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
                        result.Add($"{source}->{target}");
                    else
                        result.Add(el.GetRawText());
                }
                else
                    result.Add(el.GetRawText());
            }
            return (T[])(object)result.ToArray();
        }

        // Default: manually deserialize array elements to avoid converter recursion
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var result = new List<T>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var item = el.Deserialize<T>(options);
                if (item != null) result.Add(item);
            }
            return result.ToArray();
        }

        return [];
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}
