using System.Collections.Concurrent;

namespace Shopfront.Payments;

public class FakePaymentProvider : IPaymentProvider
{
    public const string DeclinedToken = "tok_chargeDeclined";

    private readonly ConcurrentDictionary<string, ChargeResult> byKey = new();
    private int charges;

    public Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        if (idempotencyKey is not null && byKey.TryGetValue(idempotencyKey, out var earlier))
            return Task.FromResult(earlier);

        if (paymentToken == DeclinedToken)
            throw new PaymentDeclinedException("The card was declined.");

        if (Interlocked.Increment(ref charges) % 5 == 0)
            throw new TimeoutException("The payment provider did not answer.");

        var transactionId = $"tx_{Guid.NewGuid():N}"[..11];
        var charge = new ChargeResult(
            transactionId,
            $"https://pay.example/receipts/{transactionId}?key={Guid.NewGuid():N}"
        );
        if (idempotencyKey is not null)
            byKey[idempotencyKey] = charge;
        return Task.FromResult(charge);
    }
}
