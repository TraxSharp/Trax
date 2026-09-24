---
layout: default
title: Common Patterns
parent: Cross-Cutting
nav_order: 2
---

# Common Patterns

## Error Handling Patterns

### Train-Level Error Handling

Train-level error handling belongs in the `OnFailed` lifecycle hook. A junction that throws puts
the chain on the error track, and `OnFailed` sees the exception with the train's metadata.

```csharp
public class RobustTrain : ServiceTrain<ProcessOrderRequest, ProcessOrderResult>
{
    protected override Task<Either<Exception, ProcessOrderResult>> Junctions() =>
        Chain<ValidateOrderJunction>()
            .Chain<ProcessPaymentJunction>()
            .Chain<FulfillOrderJunction>()
            .Resolve();

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        if (exception is PaymentException payment)
            Logger?.LogWarning("Payment failed for order {OrderId}: {Error}",
                metadata.GetInput<ProcessOrderRequest>()?.OrderId, payment.Message);

        return Task.CompletedTask;
    }
}
```

To turn one exception into another, catch it in the junction that raises it and throw the
exception you want the caller to see. The chain preserves the type and message.


### Junction-Level Error Handling

```csharp
public class RobustJunction(IPaymentGateway PaymentGateway) : Junction<PaymentRequest, PaymentResult>
{
    public override async Task<PaymentResult> Run(PaymentRequest input)
    {
        try
        {
            var result = await PaymentGateway.ProcessAsync(input);
            return result;
        }
        catch (TimeoutException ex)
        {
            // Throw a meaningful error
            throw new PaymentException("Payment gateway timed out", ex);
        }
    }
}
```

## Cancellation Patterns

### Passing Tokens from ASP.NET Controllers

ASP.NET Core provides a `CancellationToken` that fires when the HTTP request is aborted:

```csharp
[HttpPost("orders")]
public async Task<IActionResult> CreateOrder(
    CreateOrderRequest request,
    CancellationToken cancellationToken)
{
    var result = await trainBus.RunAsync<OrderResult>(request, cancellationToken);
    return Ok(result);
}
```

### Using the Token in Junctions

Access `this.CancellationToken` inside any junction to pass it to async operations:

```csharp
public class QueryDatabaseJunction(IDataContext context) : Junction<UserId, User>
{
    public override async Task<User> Run(UserId input)
    {
        return await context.Users
            .FirstOrDefaultAsync(u => u.Id == input.Value, CancellationToken)
            ?? throw new NotFoundException($"User {input.Value} not found");
    }
}
```

### Checking Cancellation in Long-Running Junctions

For junctions that iterate over large collections, check cancellation periodically:

```csharp
public class BatchProcessJunction : Junction<BatchInput, BatchResult>
{
    public override async Task<BatchResult> Run(BatchInput input)
    {
        var results = new List<ItemResult>();

        foreach (var item in input.Items)
        {
            CancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessItem(item));
        }

        return new BatchResult(results);
    }
}
```

*Full details: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens)*

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
