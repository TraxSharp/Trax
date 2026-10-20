namespace Shopfront.Machines.Checkout.Models;

public sealed record PaymentInput
{
    public string PaymentToken { get; init; } = "";
}
