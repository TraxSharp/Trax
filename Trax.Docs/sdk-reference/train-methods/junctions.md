---
layout: default
title: Junctions
parent: Train Methods
grand_parent: SDK Reference
nav_order: 0
---

# Junctions

Override `Junctions()` to define the train's route, the sequence of junctions it passes through. This is the primary way to compose junctions in a train.

## Signature

```csharp
// Train<TInput, TReturn>, inherited unchanged by ServiceTrain<TIn, TOut>
protected virtual Task<Either<Exception, TReturn>> Junctions()
```

## Returns

`Task<Either<Exception, TReturn>>`, the train's railway result. The chain ends in [`Resolve()`](/docs/sdk-reference/train-methods/resolve), which takes `TReturn` out of Memory, or carries the exception that stopped the chain as `Left`.

## Examples

### Basic Train

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>().Resolve();
}
```

### With ShortCircuit and Extract

All chain methods (`Chain`, `ShortCircuit`, `Extract`, `AddServices`) are available as protected methods on the train:

```csharp
public class ProcessOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        ShortCircuit<CheckCacheJunction>()
            .Chain<ValidateOrderJunction>()
            .Extract<OrderInput, OrderDetails>()
            .Chain<ProcessPaymentJunction>()
            .Resolve();
}
```

### With AddServices

```csharp
public class NotifyTrain(ISlackClient slack) : ServiceTrain<NotifyInput, Unit>
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        AddServices<ISlackClient>(slack)
            .Chain<SendNotificationJunction>()
            .Resolve();
}
```

## Behavior

1. The framework seeds Memory with the train input (under its declared type, its runtime type and that type's interfaces) and `Unit` before `Junctions()` executes.
2. Chain methods are called as protected methods on the train itself. `Chain`, `IChain` and `ShortCircuit` return a `MonadTask<TInput, TReturn>`, an awaitable wrapper that keeps the fluent surface across async links; `Extract` and `AddServices` on the train return a `Monad<TInput, TReturn>`.
3. The final `.Resolve()` awaits the chain and returns `Either<Exception, TReturn>`, following the priority exception > short-circuit value > Memory lookup.
4. If a junction throws, the remaining junctions are skipped and the exception comes back as `Left`. An exception thrown by `Junctions()` itself is caught and returned as `Left` too.
5. `Run()` unwraps the result and rethrows a `Left`; `RunEither()` returns it as is.

The same `Junctions()` is also read, without running anything, by the startup chain check. A body must therefore be a declaration and nothing else. The host refuses to start when `Junctions()`:

- reads `TrainInput` or `TrainOutput`
- awaits something before returning, which means it does work
- returns a result directly (`Task.FromResult(value)`) instead of ending in `Resolve()`
- ends in `Resolve(value)`
- uses `IChain<T>` or `AddServices<T>` with a class instead of an interface
- short-circuits with a junction whose output cannot be the train's return type

A train with no junctions ends in `Task.FromResult(Resolve())`. The full list of faults, and the Memory rules the check replays, are in [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic).

## There is no alternative to Junctions()

`RunInternal` is private and `Activate` is internal, so a chain cannot be assembled in code. A
chain built imperatively has no single shape, which would put it out of reach of the check a host
runs over every train before it serves traffic.

Everything that used to justify reaching for `RunInternal` has a place in the chain:

| What you need | Where it goes |
|---|---|
| Logic before or after the chain | A junction at the head or the tail of it |
| Extra objects in Memory | A junction whose return value is that object, or `AddServices<IService>(value)` / `Extract<TIn, TOut>(value)` in the chain |
| Returning a failure | A junction throws; the chain turns it into `Left` |
| Async setup | A junction, which is async already |
| Merging a nested train's result | A junction that calls `TrainBus` and returns the merged value |

```csharp
public class ParentTrain : ServiceTrain<ParentInput, ParentResult>, IParentTrain
{
    protected override Task<Either<Exception, ParentResult>> Junctions() =>
        Chain<RunChildTrain>().Chain<ValidateJunction>().Resolve();
}

internal class RunChildTrain(ITrainBus trainBus) : Junction<ParentInput, ParentResult>
{
    public override async Task<ParentResult> Run(ParentInput input)
    {
        var childResult = await trainBus.RunAsync<ChildResult>(
            new ChildRequest { Data = input.ChildData }, CancellationToken);

        return new ParentResult { ParentData = input.ParentData, ChildResult = childResult };
    }
}
```

The child runs as a train of its own and is not linked to the parent run. `TrainBus.RunAsync` takes an optional `Metadata`, but that is a pre-created `Pending` record for the train to run as, the way the scheduler uses it, not a parent: passing the running parent's metadata is refused with a `TrainException`.

## Remarks

- Upgrading a train that overrode `RunInternal` or called `Activate`: see [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate).
- `TrainInput` and `TrainOutput` throw while the chain is being declared. A chain that branched on its input would have no single shape, so the shape checked at startup need not be the one that runs.
