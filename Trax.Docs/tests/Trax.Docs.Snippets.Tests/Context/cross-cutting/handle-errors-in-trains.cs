// What the examples on cross-cutting/handle-errors-in-trains.md take as given: the request and
// result types, the payment gateway and the junctions the trains chain.
using Trax.Core.Junction;

public record PaymentRequest(string OrderId, decimal Amount);

public record PaymentResult(string TransactionId);

public record ProcessOrderRequest(string OrderId);

public record ProcessOrderResult(string OrderId);

public record QuoteRequest(string Sku);

public class PaymentException(string message, Exception inner) : Exception(message, inner);

public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}

public class ValidateOrderJunction : Junction<ProcessOrderRequest, PaymentRequest>
{
    public override Task<PaymentRequest> Run(ProcessOrderRequest input) =>
        Task.FromResult(new PaymentRequest(input.OrderId, 10m));
}

public class FulfillOrderJunction : Junction<PaymentResult, ProcessOrderResult>
{
    public override Task<ProcessOrderResult> Run(PaymentResult input) =>
        Task.FromResult(new ProcessOrderResult(input.TransactionId));
}

public class PriceQuoteJunction : Junction<QuoteRequest, decimal>
{
    public override Task<decimal> Run(QuoteRequest input) => Task.FromResult(9.99m);
}
