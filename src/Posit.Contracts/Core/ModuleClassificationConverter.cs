using System.Text.Json;
using System.Text.Json.Serialization;

namespace Posit.Contracts.Core;

/// <summary>
/// Custom JSON converter for ModuleClassification that accepts the many
/// variants the model might return: "logic", "dafny" (legacy), "io-shell",
/// "io_shell", "IO", "IOShell", "mixed", etc. Normalizes to the enum value.
/// </summary>
public sealed class ModuleClassificationConverter : JsonConverter<ModuleClassification>
{
    public override ModuleClassification Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()?.Trim().ToLowerInvariant() ?? "";
        return value switch
        {
            "logic" or "dafny" or "d" or "verified" or "proof" or "core" => ModuleClassification.Logic,
            "io-shell" or "io_shell" or "ioshell" or "io" or "shell" or "unverified" or "io-shell-only" => ModuleClassification.IoShell,
            "mixed" or "partial" or "split" => ModuleClassification.Mixed,
            "" => ModuleClassification.IoShell, // default to safe
            _ => ModuleClassification.IoShell   // unknown → safe default
        };
    }

    public override void Write(Utf8JsonWriter writer, ModuleClassification value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ModuleClassification.Logic => "logic",
            ModuleClassification.IoShell => "io-shell",
            ModuleClassification.Mixed => "mixed",
            _ => "io-shell"
        });
    }
}