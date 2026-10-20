namespace Shopfront.Payments;

public interface IPaymentProvider
{
    // A charge with an idempotency key the provider has seen is answered with that charge, not
    // made again.
    Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    );
}
