namespace Posit.Contracts.Core;

public record BudgetRemaining
{
    public required decimal Amount { get; init; }
    public required decimal Cap { get; init; }
}