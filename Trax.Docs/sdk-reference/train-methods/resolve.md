---
layout: default
title: Resolve
parent: Train Methods
grand_parent: SDK Reference
nav_order: 7
---

# Resolve

Ends a chain, taking the train's `TReturn` out of Memory. Follows a priority chain: exception >
short-circuit value > Memory lookup.

Every chain ends with it. On a chain that names junctions it is the last call in the fluent
sequence; on a chain that names none, because the train's return type is already in Memory as the
input type or as `Unit`, it is the only call.

## Signature

```csharp
// On the train: ends a chain that names no junctions
protected Either<Exception, TReturn> Resolve()

// On MonadTask<TInput, TReturn>, returned by Chain, IChain and ShortCircuit
public Task<Either<Exception, TReturn>> Resolve()
public Task<Either<Exception, TReturn>> Resolve(Either<Exception, TReturn> returnType)

// On Monad<TInput, TReturn>, returned by Extract and AddServices on the train
public Either<Exception, TReturn> Resolve()
public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType)
```

The train's own `Resolve()` has no overload taking a value. A declaration says which junctions
run, so stating a result directly would let it return something the junctions it names never
produced. The chain types `Monad` and `MonadTask` do carry a public `Resolve(returnType)`
overload, but a `Junctions()` that ends in it is refused by the startup chain check, and the host
will not start. It exists for code that drives a `Monad` directly, outside a train's declaration.

## Returns

`Either<Exception, TReturn>` (wrapped in a `Task` on `MonadTask`), the train result. `Left`
contains the exception on failure; `Right` contains the `TReturn` value on success. A chain whose
last step is synchronous (`Resolve()` on the train, or after `Extract` with no junction) is
wrapped in `Task.FromResult` to match the `Junctions()` return type.

## Examples

### Ending a chain of junctions

```csharp
protected override Task<Either<Exception, OrderResult>> Junctions() =>
    Chain<ValidateOrder>()
        .Chain<ProcessPayment>()   // Stores OrderResult in Memory
        .Resolve();                // Takes OrderResult back out
```

### A chain that names no junctions

The train's return type is already in Memory, so there is nothing to chain.

```csharp
public class EchoTrain : ServiceTrain<string, string>, IEchoTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Task.FromResult(Resolve());
}
```

## Resolution Priority

`Resolve()` follows this order:

1. **Exception**: if any junction set an exception, return `Left(exception)`.
2. **Short-circuit value**: if a [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) junction returned `Right`, return `Right(shortCircuitValue)`.
3. **Memory lookup**: take `TReturn` from Memory by its exact type.
4. **Container**: if Memory does not hold it, ask the service provider the chain carries.
5. **Fallback**: if neither has it, return `Left(TrainException("Could not find type: (TReturn)."))`.

## Remarks

- `Resolve()` does not throw. It returns an `Either`. The calling `Run()` method unwraps the Either and throws if needed.
- The parameterized `Resolve(returnType)` on `Monad` and `MonadTask` is simpler: it returns the chain's exception if one exists, otherwise the provided value. It does **not** check short-circuit or Memory. Inside `Junctions()` it is refused at startup, as is returning a value without `Resolve()` at all (`Task.FromResult(value)`).
