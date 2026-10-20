using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record ShippingContext
{
    public List<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? ParcelId { get; init; }
}
