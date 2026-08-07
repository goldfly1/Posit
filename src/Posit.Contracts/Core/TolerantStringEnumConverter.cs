using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Posit.Contracts.Core;

/// <summary>
/// A tolerant string-enum converter factory that accepts both camelCase and kebab-case
/// enum names, plus case-insensitive matches. Replaces the strict JsonStringEnumConverter
/// so downstream phases can deserialize model output even when the case style drifts.
/// Handles nullable enums (e.g. PhaseStatus?) as well as regular enums.
/// </summary>
public sealed class TolerantStringEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        if (underlying == typeof(ModuleClassification))
            return false;
        return underlying.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        var converterType = typeof(TolerantEnumConverterImpl<>).MakeGenericType(underlying);
        var baseConverter = (JsonConverter)Activator.CreateInstance(converterType)!;

        if (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var nullableConverterType = typeof(NullableEnumConverterImpl<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(nullableConverterType, baseConverter)!;
        }

        return baseConverter;
    }

    private sealed class NullableEnumConverterImpl<T> : JsonConverter<T?> where T : struct, Enum
    {
        private readonly JsonConverter<T> _baseConverter;

        public NullableEnumConverterImpl(JsonConverter baseConverter)
        {
            _baseConverter = (JsonConverter<T>)baseConverter;
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            return _baseConverter.Read(ref reader, typeof(T), options);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            _baseConverter.Write(writer, value.Value, options);
        }
    }

    private sealed class TolerantEnumConverterImpl<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(raw))
                return default;

            // Normalize: kebab-case / snake_case -> PascalCase-ish, then try exact and ignore-case
            var candidate = NormalizeEnumName(raw);

            // Try exact match first
            if (Enum.TryParse<T>(candidate, out var exact))
                return exact;

            // Then ignore case on normalized name
            if (Enum.TryParse<T>(candidate, ignoreCase: true, out var fuzzy))
                return fuzzy;

            // Try each enum member name directly with ignore-case (handles "Dafny" for Dafny, etc.)
            foreach (var name in Enum.GetNames<T>())
            {
                if (name.Equals(raw, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse<T>(name);
            }

            // Unknown value: return default (do not throw — model may invent new enum values)
            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        }

        private static string NormalizeEnumName(string input)
        {
            var sb = new StringBuilder(input.Length);
            bool nextUpper = true;
            foreach (var c in input)
            {
                if (c == '-' || c == '_' || c == ' ')
                {
                    nextUpper = true;
                }
                else if (nextUpper)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    nextUpper = false;
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }
    }
}
