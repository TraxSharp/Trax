---
layout: default
title: Common Patterns
parent: Usage Guide
nav_order: 10
---

# Common Patterns

## Error Handling Patterns

### Workflow-Level Error Handling

```csharp
public class RobustWorkflow : ServiceTrain<ProcessOrderRequest, ProcessOrderResult>
{
    protected override async Task<Either<Exception, ProcessOrderResult>> RunInternal(ProcessOrderRequest input)
    {
        try
        {
            return await Activate(input)
                .Chain<ValidateOrderStep>()
                .Chain<ProcessPaymentStep>()
                .Chain<FulfillOrderStep>()
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

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

### Step-Level Error Handling

```csharp
public class RobustStep(IPaymentGateway PaymentGateway) : Step<PaymentRequest, PaymentResult>
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
    var result = await workflowBus.RunAsync<OrderResult>(request, cancellationToken);
    return Ok(result);
}
```

### Using the Token in Steps

Access `this.CancellationToken` inside any step to pass it to async operations:

```csharp
public class QueryDatabaseStep(IDataContext context) : Step<UserId, User>
{
    public override async Task<User> Run(UserId input)
    {
        return await context.Users
            .FirstOrDefaultAsync(u => u.Id == input.Value, CancellationToken)
            ?? throw new NotFoundException($"User {input.Value} not found");
    }
}
```

### Checking Cancellation in Long-Running Steps

For steps that iterate over large collections, check cancellation periodically:

```csharp
public class BatchProcessStep : Step<BatchInput, BatchResult>
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

*Full details: [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %})*
