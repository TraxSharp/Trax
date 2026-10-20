using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required List<OrderLine> Lines { get; init; }
    public required string PaymentToken { get; init; }
}
