// The payment gateway the page's effect calls: the reader's own client for their payment provider.
public interface IPaymentGateway
{
    Task<string> Charge(
        IReadOnlyList<string> items,
        string idempotencyKey,
        CancellationToken cancellationToken
    );
}
