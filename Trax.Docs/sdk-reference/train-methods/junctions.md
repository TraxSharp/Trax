---
layout: default
title: Junctions
parent: Train Methods
grand_parent: SDK Reference
nav_order: 0
---

# Junctions

Override `Junctions()` to define the train's route — the sequence of junctions it passes through. This is the primary way to compose junctions in a train.

## Signature

```csharp
// Train<TInput, TReturn>
protected virtual TReturn Junctions()

// ServiceTrain<TIn, TOut> — same signature
protected virtual TOut Junctions()
```

## Returns

`TReturn` — the train's output type. The return value is resolved automatically from Memory via an implicit conversion on `Monad`. You do not need to call `Resolve()` or wrap the result in `Either`.

## Examples

### Basic Train

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override User Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>();
}
```

### With ShortCircuit and Extract

All chain methods (`Chain`, `ShortCircuit`, `Extract`, `AddServices`) are available as protected methods on the train:

```csharp
public class ProcessOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override OrderResult Junctions() =>
        ShortCircuit<CheckCacheJunction>()
            .Chain<ValidateOrderJunction>()
            .Extract<OrderInput, OrderDetails>()
            .Chain<ProcessPaymentJunction>();
}
```

### With AddServices

```csharp
public class NotifyTrain(ISlackClient slack) : ServiceTrain<NotifyInput, Unit>
{
    protected override Unit Junctions() =>
        AddServices<ISlackClient>(slack)
            .Chain<SendNotificationJunction>();
}
```

## Behavior

1. The framework calls `Activate(input)` automatically before `Junctions()` executes, seeding Memory with the train input and `Unit`.
2. Chain methods are called as protected methods on the train itself (not on a separate `Monad` returned by `Activate`).
3. The final chain call returns a `Monad<TInput, TReturn>`, which is implicitly converted to `TReturn` by extracting the result from Memory.
4. If any junction threw an exception, the implicit conversion returns `default(TReturn)` and the framework handles the exception via the railway error path.
5. The `Run()` / `RunEither()` public API is unchanged for callers.

## When to Use RunInternal Instead

`Junctions()` covers the common case. Override `RunInternal` when you need:

- **Custom logic before or after the chain** — try/catch around the chain, logging, or conditional branching
- **Extra objects in Memory** — `Activate(input, extraObject)` passes additional objects into Memory
- **Manual Either construction** — returning `Left(exception)` or `Right(value)` directly
- **Async setup** — awaiting something before building the chain
- **Combining nested train results** — calling `TrainBus.RunAsync` and merging the result with the chain via `Resolve(explicitValue)`

```csharp
protected override async Task<Either<Exception, ParentResult>> RunInternal(ParentInput input)
{
    var childResult = await TrainBus.RunAsync<ChildResult>(
        new ChildRequest { Data = input.ChildData }, Metadata);

    return Activate(input)
        .Chain<ValidateJunction>()
        .Resolve(new ParentResult
        {
            ParentData = input.ParentData,
            ChildResult = childResult
        });
}
```

## Remarks

- `Junctions()` and `RunInternal` are mutually exclusive — override one or the other, not both. If both are overridden, `RunInternal` takes precedence.
- The implicit conversion from `Monad<TInput, TReturn>` to `TReturn` calls `Resolve()` internally, following the same resolution priority: exception > short-circuit value > Memory lookup.
- Backwards compatible: existing trains using `RunInternal` continue to work without changes.
