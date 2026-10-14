---
title: Junctions
description: "Reference for the Junctions() override that declares a train's chain: its signature, what it returns, and what the startup chain check refuses in it."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Train
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 0
---

# Junctions

The override that declares a train's chain: the junctions it passes through, in order, and the steps between them. Every train overrides it, and it is the only way to declare a chain.

## Signature

```csharp fragment
// Train<TInput, TReturn>, inherited unchanged by ServiceTrain<TIn, TOut>
protected virtual Task<Either<Exception, TReturn>> Junctions();
```

## Returns

`Task<Either<Exception, TReturn>>`, the train's railway result. The chain ends in [Resolve()](/docs/sdk-reference/train-methods/resolve), which takes `TReturn` out of Memory, or carries the exception that stopped the chain as `Left`.

## Behavior

1. The train seeds Memory with its input (under its declared type, its runtime type and that type's interfaces) and with `Unit`, then calls `Junctions()`.
2. The chain methods are protected methods on the train. `Chain`, `IChain`, `ShortCircuit` and the routing steps return a [MonadTask](/docs/sdk-reference/trains-and-junctions/monad-task); `Extract` and `AddServices` called on the train return a `Monad`.
3. `Resolve()` awaits the chain and returns `Either<Exception, TReturn>`.
4. A junction that throws skips the rest of the chain and its exception comes back as `Left`. An exception `Junctions()` itself throws is returned as `Left` too.
5. [Run](/docs/sdk-reference/train-methods/run) unwraps the result and rethrows a `Left`; [RunEither](/docs/sdk-reference/train-methods/run-either) returns it as is.
6. The base implementation throws `NotImplementedException`.

The same `Junctions()` is read without running anything by [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain), which the host's startup chain check calls for every registered train. The host refuses to start when `Junctions()`:

- reads `TrainInput` or `TrainOutput`, which throw `ChainDeclarationException` while the chain is read
- awaits something before returning
- returns a result directly (`Task.FromResult(value)`) instead of ending in `Resolve()`
- ends in `Resolve(value)`
- uses `IChain<T>` or `AddServices<T>` with a class instead of an interface
- passes `AddServices` a null service, such as a field assigned later in `OnStarted`
- short-circuits with a junction whose output cannot be the train's return type

A train with no junctions ends in `Task.FromResult(Resolve())`. The full list of faults, and the Memory rules the check replays, are in [The host checks every chain before it serves traffic](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic). A train that used to override `RunInternal` or call `Activate` is upgraded with [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate).

## Examples

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class PlaceOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>().Chain<ProcessPayment>().Resolve();
}
```

With `ShortCircuit`, `Extract` and `AddServices`:

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class FulfilOrderTrain(INotificationService notifications)
    : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        AddServices<INotificationService>(notifications)
            .ShortCircuit<CheckCache>()
            .Extract<OrderInput, OrderDetails>()
            .Chain<ProcessOrder>()
            .Chain<SendConfirmation>()
            .Resolve();
}
```

## See also

- [Move work around a chain into junctions](/docs/core/move-work-into-the-chain)
- [Trains and Junctions](/docs/core/trains-and-junctions#declaring-the-chain), why the declaration is the only way
- [Train Methods](/docs/sdk-reference/train-methods), every step a chain can take
