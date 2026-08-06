namespace Posit.Contracts.Core;

public record ModelRoute
{
    public required ModelTier Tier { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public int MaxOutputTokens { get; init; }
    public double Temperature { get; init; } = 0.2;
    public RoutingStrategy Strategy { get; init; } = RoutingStrategy.Static;
}