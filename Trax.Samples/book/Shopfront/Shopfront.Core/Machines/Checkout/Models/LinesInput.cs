namespace Shopfront.Machines.Checkout.Models;

public sealed record LinesInput
{
    public IReadOnlyList<CartLine> Lines { get; init; } = [];
}
