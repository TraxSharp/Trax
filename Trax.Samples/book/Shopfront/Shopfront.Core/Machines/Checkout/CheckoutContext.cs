using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record CheckoutContext
{
    public IReadOnlyList<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
}
