---
layout: default
title: MonadTask
description: Reference for MonadTask, the awaitable struct each chain step returns so Chain, IChain and ShortCircuit calls continue fluently across async junctions.
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 6
---

# MonadTask

The type a chain step returns. `Chain`, `IChain` and `ShortCircuit` return `MonadTask<TInput, TReturn>`, an awaitable struct that queues each link to run after the ones before it and keeps the fluent surface, so the next call names only the junction type. You rarely name it: it is what makes `Chain<A>().Chain<B>().Resolve()` compile.

## Signature

```csharp
namespace Trax.Core.Train;

public readonly struct MonadTask<TInput, TReturn>
{
    // The chain methods, each returning the next link
    public MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> IChain<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> Extract<TIn, TOut>();
    public MonadTask<TInput, TReturn> AddServices<T1>(T1 service);  // up to seven services
    public Task<Either<Exception, TReturn>> Resolve();
    // ...plus the instance and explicit-type overloads, as on Train

    // Awaiting
    public TaskAwaiter<Monad<TInput, TReturn>> GetAwaiter();
    public ConfiguredTaskAwaitable<Monad<TInput, TReturn>> ConfigureAwait(bool continueOnCapturedContext);
    public Task<Monad<TInput, TReturn>> AsTask();
    public static implicit operator Task<Monad<TInput, TReturn>>(MonadTask<TInput, TReturn> mt);
}
```

Every member is public: the struct is the receiver of the chain. On [Train](/docs/sdk-reference/trains-and-junctions/train) the methods that start a chain are `protected`.

## Members

| Member | Description |
|--------|-------------|
| `Chain`, `IChain`, `ShortCircuit`, `Extract`, `AddServices` | Queue the next link. See [Chain](/docs/sdk-reference/train-methods/chain), [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit), [Extract](/docs/sdk-reference/train-methods/extract) and [AddServices](/docs/sdk-reference/train-methods/add-services). A link is skipped if an earlier one failed. |
| `Resolve()` | Ends the chain and returns `Task<Either<Exception, TReturn>>`, which is what `Junctions()` returns. See [Resolve](/docs/sdk-reference/train-methods/resolve). |
| `GetAwaiter()` / `ConfigureAwait` | Await the links queued so far. A failed junction does not throw here; the failure stays in the monad until `Resolve()`. |
| `AsTask()` / implicit conversion | The task behind the chain, for APIs that take a `Task<T>`. |

## Remarks

- `Junctions()` is a declaration: return the chain, ending in `Resolve()`. Awaiting a `MonadTask` inside `Junctions()` is work, not declaration, and the [startup chain check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) refuses a `Junctions()` that awaits before returning.
- `Monad<TInput, TReturn>` (namespace `Trax.Core.Monad`) is what `Extract` and `AddServices` return when called directly on the train. It has the same chain methods, and its `Chain` returns a `MonadTask`. It has no public constructor.

## Package

```
dotnet add package Trax.Core
```
