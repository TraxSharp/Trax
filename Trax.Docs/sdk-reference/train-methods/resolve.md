---
title: Resolve
description: "Reference for Resolve, which ends every chain by taking the train's return value from Memory, in the order exception, short-circuit value, Memory, container."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.Resolve.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 6
---

# Resolve

Ends a chain, taking the train's `TReturn` out of Memory. Every chain ends with it: on a chain that names junctions it is the last call, and on a chain that names none, because the return type is already in Memory as the input type or as `Unit`, it is the only call.

## Signatures

```csharp fragment
// Train<TInput, TReturn>: ends a chain that names no junctions
protected Either<Exception, TReturn> Resolve();

// MonadTask<TInput, TReturn>, returned by Chain, IChain, ShortCircuit and the routing steps
public Task<Either<Exception, TReturn>> Resolve();
public Task<Either<Exception, TReturn>> Resolve(Either<Exception, TReturn> returnType);

// Monad<TInput, TReturn>, returned by Extract and AddServices on the train
public Either<Exception, TReturn> Resolve();
public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType);
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `returnType` | `Monad` and `MonadTask` only: the result to return when the chain has no exception |

## Returns

`Either<Exception, TReturn>`, wrapped in a `Task` on `MonadTask`: `Left` holding the exception on failure, `Right` holding the result. A chain whose last step is synchronous (`Resolve()` on the train, or after `Extract` with no junction) is wrapped in `Task.FromResult` to match the `Junctions()` return type.

## Behavior

`Resolve()` returns the first of:

1. **Exception**: the exception a step set, as `Left`.
2. **Short-circuit value**: the value a [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) junction returned, as `Right`.
3. **Memory**: the `TReturn` in Memory, by its exact type.
4. **Container**: a `TReturn` from the service provider the chain carries.
5. **Missing**: a `TrainException` as `Left`, naming the train and the type: `Train 'CreateUserTrain' needs 'User' for Resolve() to return, but nothing earlier in the chain produced one and it is not registered in the container. Register it or chain a junction that outputs it first.` A missing value type (`int`, `Guid`) fails too, rather than resolving to its default.

`Resolve()` does not throw; [Run](/docs/sdk-reference/train-methods/run) unwraps the result and throws a `Left`.

`Resolve(returnType)` returns the chain's exception if it has one, otherwise `returnType`; it reads neither the short-circuit value nor Memory. The train's own `Resolve()` has no such overload. Inside `Junctions()`, a chain that ends in `Resolve(returnType)`, or that returns a value without `Resolve()` (`Task.FromResult(value)`), is refused by the startup chain check; the overload exists for code that drives a `Monad` outside a train's declaration.

## Examples

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class PlaceOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>()
            .Chain<ProcessPayment>()   // stores OrderResult in Memory
            .Resolve();                // takes OrderResult back out
}
```

A chain that names no junctions, because the return type is the input type:

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public interface IEchoTrain : IServiceTrain<string, string>;

public class EchoTrain : ServiceTrain<string, string>, IEchoTrain
{
    protected override Task<Either<Exception, string>> Junctions() => Task.FromResult(Resolve());
}
```

## See also

- [Junctions](/docs/sdk-reference/train-methods/junctions)
- [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit)
