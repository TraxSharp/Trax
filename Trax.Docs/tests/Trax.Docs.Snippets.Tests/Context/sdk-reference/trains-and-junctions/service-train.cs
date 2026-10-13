// What the example on sdk-reference/trains-and-junctions/service-train.md names but does not
// declare: the alerting service and the three junctions of its chain.
using Trax.Effect.Services.EffectJunction;

public interface IOrderAlerts
{
    Task OrderFailed(string customerId, string reason, CancellationToken ct);
}

public class ValidateOrder : EffectJunction<CreateOrderInput, ValidatedOrder>
{
    public override Task<ValidatedOrder> Run(CreateOrderInput input) =>
        Task.FromResult(new ValidatedOrder(input.CustomerId, input.Total));
}

public class ChargePayment(IPaymentGateway gateway) : EffectJunction<ValidatedOrder, PaymentReceipt>
{
    public override Task<PaymentReceipt> Run(ValidatedOrder order) =>
        gateway.Charge(order.CustomerId, order.Total, CancellationToken);
}

public class PersistOrder(IOrderStore store) : EffectJunction<PaymentReceipt, OrderResult>
{
    public override async Task<OrderResult> Run(PaymentReceipt receipt) =>
        new((await store.Save(receipt, CancellationToken)).Id);
}
