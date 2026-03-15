---
layout: default
title: Building Chains
parent: Core
nav_order: 2
---

# Building Chains

Every train's `RunInternal` method is a chain: `Activate` seeds Memory, `.Chain<T>()` adds junctions, and `.Resolve()` returns the result.

## Chain

`.Chain<TStep>()` is the primary way to add a junction to a train's route. It resolves the step, pulls its input from [Memory](memory.md), runs it, and stores the output back in Memory.

```csharp
Activate(input)
    .Chain<ValidateEmailStep>()
    .Chain<CreateUserStep>()
    .Chain<SendEmailStep>()
    .Resolve();
```

*SDK Reference: [Activate]({{ site.baseurl }}{% link sdk-reference/train-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link sdk-reference/train-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link sdk-reference/train-methods/resolve.md %})*

For all overloads, type parameter constraints, and step-wiring behavior, see [SDK Reference: Chain]({{ site.baseurl }}{% link sdk-reference/train-methods/chain.md %}). The [Analyzer](analyzer.md) catches missing types at compile time, so you'll see these errors in your IDE before you ever run the code.

### Railway Behavior

If a previous step switched the train to the left track, `.Chain<TStep>()` is skipped entirely. The exception propagates through the chain until it reaches `.Resolve()`, which returns it as `Left(exception)`.

```csharp
Activate(input)
    .Chain<ValidateEmailStep>()    // Throws ValidationException
    .Chain<CreateUserStep>()       // Skipped
    .Chain<SendEmailStep>()        // Skipped
    .Resolve();                    // Returns Left(ValidationException)
```

## Resolve

`.Resolve()` terminates the chain and returns `Either<Exception, TReturn>`. Every train's `RunInternal` ends with a call to `Resolve`.

```csharp
protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
    => Activate(input)
        .Chain<ValidateEmailStep>()
        .Chain<CreateUserStep>()
        .Chain<SendEmailStep>()
        .Resolve();
```

`Resolve` checks for a captured exception, then a [ShortCircuit](#shortcircuit) value, then looks up `TReturn` in [Memory](memory.md) — in that order. See [SDK Reference: Resolve]({{ site.baseurl }}{% link sdk-reference/train-methods/resolve.md %}) for the full resolution priority and error behavior.

The [Analyzer](analyzer.md) catches missing return types at compile time with **CHAIN002**.

### The Parameterized Overload

There's a second overload that takes an `Either<Exception, TReturn>` directly:

```csharp
protected override async Task<Either<Exception, ParentResult>> RunInternal(ParentRequest input)
{
    var childResult = await TrainBus.RunAsync<ChildResult>(
        new ChildRequest { Data = input.ChildData },
        Metadata
    );

    return Activate(input)
        .Chain<ValidateStep>()
        .Resolve(new ParentResult
        {
            ParentData = input.ParentData,
            ChildResult = childResult
        });
}
```

This skips the Memory lookup — you're providing the result directly. If an exception exists from the chain, it still takes precedence and the provided value is ignored. This is useful when you need to construct the return value manually, like combining results from nested trains with the chain's output.

## ShortCircuit

`.ShortCircuit<TStep>()` lets a step take the express route — capturing a result for early return. If the step returns a value of the train's return type, that value is stored as the short-circuit result and `Resolve()` will return it instead of doing a Memory lookup. If the step throws, the train continues normally.

> **Note:** Subsequent `Chain` calls after a successful `ShortCircuit` still execute — the short-circuit value only affects `Resolve()`. If you need to truly skip remaining steps, combine `ShortCircuit` with a conditional pattern or the railway error path.

```csharp
public class ProcessOrderTrain : ServiceTrain<OrderRequest, OrderResult>
{
    protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderRequest input)
        => Activate(input)
            .Chain<ValidateOrderStep>()
            .ShortCircuit<CheckCacheStep>()  // If cached, capture result for Resolve
            .Chain<CalculatePricingStep>()   // Still executes (short-circuit only affects Resolve)
            .Chain<ProcessPaymentStep>()     // Still executes (short-circuit only affects Resolve)
            .Chain<SaveOrderStep>()
            .Resolve();
}
```

> **This behavior is intentionally inverted from Chain.** A `Chain` step that throws switches the train to the left track with an error. A `ShortCircuit` step that throws means "no short-circuit available, keep going." The exception is swallowed, not propagated.

See [SDK Reference: ShortCircuit]({{ site.baseurl }}{% link sdk-reference/train-methods/short-circuit.md %}) for all overloads, the step signature, and a full example.

**When to use it:**
- **Caching** — return a cached result if available, otherwise compute it
- **Feature flags** — return a default result if a feature is disabled
- **Early exits** — skip expensive processing when a precondition is already satisfied

## Extract

`.Extract<TSource, TTarget>()` pulls a nested value out of an object in [Memory](memory.md). It finds the `TSource` object, looks for a property or field of type `TTarget`, and stores that value in Memory under the `TTarget` type.

```csharp
Activate(input)
    .Chain<LoadUserStep>()              // Returns User, stored in Memory
    .Extract<User, EmailAddress>()      // Finds EmailAddress property on User, stores it
    .Chain<ValidateEmailStep>()         // Takes EmailAddress from Memory
    .Resolve();
```

`Extract` uses reflection to find a property or field on `TSource` whose type matches `TTarget` and stores it in Memory. See [SDK Reference: Extract]({{ site.baseurl }}{% link sdk-reference/train-methods/extract.md %}) for the full search order and failure behavior.

`Extract` is a convenience for avoiding a step that exists solely to pull a property off an object. Without it, you'd write:

```csharp
public class GetUserEmailStep : Step<User, EmailAddress>
{
    public override Task<EmailAddress> Run(User input)
        => Task.FromResult(input.Email);
}
```

`.Extract<User, EmailAddress>()` does the same thing without the boilerplate. Use it when the property access is trivial. If you need any logic (null checking, transformation, validation), write a step instead.

## AddServices

`.AddServices()` puts service instances directly into [Memory](memory.md), making them available to subsequent steps. This bypasses the DI container — the instances you pass are stored as-is.

```csharp
protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
{
    var validator = new CustomValidator();
    var notifier = new SlackNotifier();

    return Activate(input)
        .AddServices<IValidator, INotifier>(validator, notifier)
        .Chain<ValidateStep>()     // Can take IValidator from Memory
        .Chain<CreateUserStep>()
        .Chain<NotifyStep>()       // Can take INotifier from Memory
        .Resolve();
}
```

Each type argument is stored in Memory with the corresponding instance. See [SDK Reference: AddServices]({{ site.baseurl }}{% link sdk-reference/train-methods/add-services.md %}) for all overloads and interface-type storage behavior.

Use `AddServices` when you need to inject runtime-created instances into the chain — objects that aren't available through the DI container or that need to be created per-execution. For standard dependencies, prefer constructor injection in your steps instead.
