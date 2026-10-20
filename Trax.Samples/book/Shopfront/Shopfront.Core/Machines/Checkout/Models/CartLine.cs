namespace Shopfront.Machines.Checkout.Models;

public sealed record CartLine
{
    public string Sku { get; init; } = "";
    public int Quantity { get; init; }
}
