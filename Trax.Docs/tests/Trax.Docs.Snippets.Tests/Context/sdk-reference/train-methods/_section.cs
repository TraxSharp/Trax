// What the examples under sdk-reference/train-methods take as given: an order domain, the
// junctions their chains name, and a registered order train. Each page's fences declare the train
// they show; types only one page needs are in that page's own context file.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record OrderDetails(string ItemId, int Quantity);

public record OrderInput(string OrderId, OrderDetails Details);

public record ValidatedOrder(string OrderId, int Quantity);

public record OrderResult(string ConfirmationId);

public interface IPaymentGateway
{
    Task<string> Charge(string orderId, CancellationToken ct);
}

public interface IInventoryService
{
    Task Reserve(string itemId, int quantity, CancellationToken ct);
}

public interface INotificationService
{
    Task Send(string message, CancellationToken ct);
}

public class ValidateOrder : Junction<OrderInput, ValidatedOrder>
{
    public override Task<ValidatedOrder> Run(OrderInput input) =>
        Task.FromResult(new ValidatedOrder(input.OrderId, input.Details.Quantity));
}

public class ProcessPayment(IPaymentGateway gateway) : Junction<ValidatedOrder, OrderResult>
{
    public override async Task<OrderResult> Run(ValidatedOrder order) =>
        new(await gateway.Charge(order.OrderId, CancellationToken));
}

public class SendConfirmation(INotificationService notifications) : Junction<OrderResult, Unit>
{
    public override async Task<Unit> Run(OrderResult result)
    {
        await notifications.Send($"Order confirmed: {result.ConfirmationId}", CancellationToken);
        return Unit.Default;
    }
}

public class CheckCache : Junction<OrderInput, OrderResult>
{
    public override Task<OrderResult> Run(OrderInput input) =>
        throw new InvalidOperationException($"No cached result for {input.OrderId}.");
}

public class ProcessOrder : Junction<OrderDetails, OrderResult>
{
    public override Task<OrderResult> Run(OrderDetails details) =>
        Task.FromResult(new OrderResult($"{details.ItemId}x{details.Quantity}"));
}

public interface IOrderTrain : IServiceTrain<OrderInput, OrderResult>;

public class OrderTrain : ServiceTrain<OrderInput, OrderResult>, IOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>().Chain<ProcessPayment>().Resolve();
}
