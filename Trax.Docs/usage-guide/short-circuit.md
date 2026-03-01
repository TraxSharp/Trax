---
layout: default
title: ShortCircuit
parent: Usage Guide
nav_order: 5
---

# ShortCircuit

`.ShortCircuit<TStep>()` lets a step end the workflow early with a valid result. If the step returns a value of the workflow's return type, that becomes the final result and remaining steps are skipped. If the step throws, the workflow continues normally.

```csharp
public class ProcessOrderWorkflow : ServiceTrain<OrderRequest, OrderResult>
{
    protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderRequest input)
        => Activate(input)
            .Chain<ValidateOrderStep>()
            .ShortCircuit<CheckCacheStep>()  // If cached, return early
            .Chain<CalculatePricingStep>()   // Skipped if cache hit
            .Chain<ProcessPaymentStep>()     // Skipped if cache hit
            .Chain<SaveOrderStep>()
            .Resolve();
}
```

*API Reference: [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [ShortCircuit]({{ site.baseurl }}{% link api-reference/workflow-methods/short-circuit.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

## The Step

> **This behavior is intentionally inverted from Chain.** A `Chain` step that throws stops the workflow with an error. A `ShortCircuit` step that throws means "no short-circuit available, keep going." The exception is swallowed, not propagated.

See [API Reference: ShortCircuit]({{ site.baseurl }}{% link api-reference/workflow-methods/short-circuit.md %}) for all overloads, the step signature, and a full example.

## When to Use It

- **Caching** — return a cached result if available, otherwise compute it
- **Feature flags** — return a default result if a feature is disabled
- **Early exits** — skip expensive processing when a precondition is already satisfied

