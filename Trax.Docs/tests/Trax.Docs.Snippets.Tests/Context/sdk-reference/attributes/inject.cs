// What the example on sdk-reference/attributes/inject.md names but does not declare.
public record ValidatedOrder(string CustomerId, decimal Total);

public record PaymentReceipt(string ChargeId);

public interface IPaymentGateway
{
    Task<PaymentReceipt> Charge(string customerId, decimal total, CancellationToken ct);
}
