namespace Shopfront.Machines.Checkout.Models;

public sealed record AddressInput
{
    public string Address { get; init; } = "";
}
