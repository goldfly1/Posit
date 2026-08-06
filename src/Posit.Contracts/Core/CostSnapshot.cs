namespace Posit.Contracts.Core;

public record CostSnapshot
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal AmountUsd { get; init; }
    public ModelTier ModelTier { get; init; }

    public static CostSnapshot Zero { get; } = new();

    public static CostSnapshot operator +(CostSnapshot a, CostSnapshot b) => new()
    {
        InputTokens = a.InputTokens + b.InputTokens,
        OutputTokens = a.OutputTokens + b.OutputTokens,
        AmountUsd = a.AmountUsd + b.AmountUsd,
        ModelTier = a.ModelTier
    };
}