namespace Posit.Contracts.Core;

public record ValidationResult
{
    public bool IsValid { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];
}