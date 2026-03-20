---
layout: default
title: Common Patterns
parent: Cross-Cutting
nav_order: 2
---

# Common Patterns

## Error Handling Patterns

### Train-Level Error Handling

```csharp
public class RobustTrain : ServiceTrain<ProcessOrderRequest, ProcessOrderResult>
{
    protected override async Task<Either<Exception, ProcessOrderResult>> RunInternal(ProcessOrderRequest input)
    {
        try
        {
            return await Activate(input)
                .Chain<ValidateOrderJunction>()
                .Chain<ProcessPaymentJunction>()
                .Chain<FulfillOrderJunction>()
                .Resolve();
        }
        catch (PaymentException ex)
        {
            // Handle payment-specific errors
            Logger?.LogWarning("Payment failed for order {OrderId}: {Error}",
                input.OrderId, ex.Message);
            return new OrderProcessingException("Payment processing failed", ex);
        }
        catch (InventoryException ex)
        {
            // Handle inventory-specific errors
            return new OrderProcessingException("Insufficient inventory", ex);
        }
    }
}
```

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

> [Activate](/docs/sdk-reference/train-methods/activate) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
