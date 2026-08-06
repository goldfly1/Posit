namespace Posit.Contracts.Core;

public sealed record ArtifactSchema
{
    public required ArtifactKind Kind { get; init; }
    public required string SchemaVersion { get; init; }
    public string PayloadClrTypeName { get; init; } = "";
    public FieldRule[] FieldRules { get; init; } = [];
}

public sealed record FieldRule(
    string FieldName,
    bool Required,
    int? MinItems = null,
    int? MaxItems = null,
    string? Pattern = null);