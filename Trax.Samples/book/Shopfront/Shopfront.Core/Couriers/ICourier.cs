namespace Shopfront.Couriers;

public interface ICourier
{
    Task<CourierRates> Rates(CancellationToken cancellationToken);

    // Buys the label for an order and hands back its tracking number. Asking again for the same
    // order hands back the first label, not a second one.
    Task<string> BuyLabel(Guid orderId, decimal price, CancellationToken cancellationToken);
}
