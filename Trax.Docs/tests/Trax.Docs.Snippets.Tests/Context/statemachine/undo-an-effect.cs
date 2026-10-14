// What the examples on statemachine/undo-an-effect.md take as given: the refund machine's enums and the
// payment gateway the refund calls.
public enum RefundState
{
    Requested,
    Refunded,
}

public enum RefundTrigger
{
    Refund,
}

public interface IPaymentGateway
{
    Task<string> Refund(string chargeId, string idempotencyKey, CancellationToken ct);
}
