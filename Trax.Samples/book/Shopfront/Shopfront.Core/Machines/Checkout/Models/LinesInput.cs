namespace Shopfront.Machines.Checkout.Models;

public sealed record LinesInput
{
    public List<CartLine> Lines { get; init; } = [];
}
