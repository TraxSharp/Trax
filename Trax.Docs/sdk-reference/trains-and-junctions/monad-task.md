---
title: MonadTask
description: "Reference for MonadTask and Monad, the types each chain step returns so Chain, IChain, ShortCircuit and the other steps continue fluently."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Extensions/MonadTaskExtensions.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 6
---

# MonadTask

The type a chain step returns. `Chain`, `IChain`, `ShortCircuit` and the routing steps return `MonadTask<TInput, TReturn>`, an awaitable struct that queues each link to run after the ones before it and keeps the fluent surface, so the next call names only the junction type. It is what makes `Chain<A>().Chain<B>().Resolve()` compile. `Monad<TInput, TReturn>` is what `Extract` and `AddServices` return when called on the train itself; it carries the same chain methods.

## Signature

```csharp fragment
namespace Trax.Core.Train;

public readonly struct MonadTask<TInput, TReturn>
{
    // The chain methods, each returning the next link
    public MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> IChain<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class;
    public MonadTask<TInput, TReturn> Extract<TIn, TOut>();
    public MonadTask<TInput, TReturn> AddServices<T1>(T1 service);
    public MonadTask<TInput, TReturn> Decide<TState>(Func<Questions<TState>, Questions<TState>> questions);
    public MonadTask<TInput, TReturn> Checkpoint<TState>();
    public Task<Either<Exception, TReturn>> Resolve();
    public Task<Either<Exception, TReturn>> Resolve(Either<Exception, TReturn> returnType);
    // ...plus the instance, explicit-type and routing overloads, as on Train

    // Awaiting
    public TaskAwaiter<Monad<TInput, TReturn>> GetAwaiter();
    public ConfiguredTaskAwaitable<Monad<TInput, TReturn>> ConfigureAwait(bool continueOnCapturedContext);
    public Task<Monad<TInput, TReturn>> AsTask();
    public static implicit operator Task<Monad<TInput, TReturn>>(MonadTask<TInput, TReturn> mt);
}
```

```csharp fragment
namespace Trax.Core.Monad;

public class Monad<TInput, TReturn>
{
    public MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class;
    public Monad<TInput, TReturn> Extract<TIn, TOut>();
    public Monad<TInput, TReturn> AddServices<T1>(T1 service);
    public Either<Exception, TReturn> Resolve();
    public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType);
    // ...and every other chain method MonadTask has
}
```

## Members

| Member | Description |
|--------|-------------|
| `Chain`, `IChain`, `ShortCircuit`, `Extract`, `AddServices` | Queue the next link. See [Chain](/docs/sdk-reference/train-methods/chain), [IChain](/docs/sdk-reference/train-methods/i-chain), [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit), [Extract](/docs/sdk-reference/train-methods/extract) and [AddServices](/docs/sdk-reference/train-methods/add-services). |
| `Decide`, `Switch`, `Gate`, `Scale`, `Parallel`, `Checkpoint` | The decision, routing, branching and checkpoint steps. See [Train Methods](/docs/sdk-reference/train-methods). |
| `Resolve()` | Ends the chain and returns `Task<Either<Exception, TReturn>>`, which is what `Junctions()` returns. See [Resolve](/docs/sdk-reference/train-methods/resolve). |
| `GetAwaiter()` / `ConfigureAwait` | Await the links queued so far. A failed junction does not throw here; the failure stays in the monad until `Resolve()`. |
| `AsTask()` / implicit conversion | The task behind the chain, for APIs that take a `Task<T>`. |

## Behavior

- Every member of `MonadTask` is public: the struct is the receiver of the chain. On [Train](/docs/sdk-reference/trains-and-junctions/train) the methods that start a chain are `protected`.
- A link queued after a failed one is skipped.
- `Monad<TInput, TReturn>` has no public constructor; the train creates one for each run. Its `Chain` returns a `MonadTask`.
- The [startup chain check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) refuses a `Junctions()` that awaits a `MonadTask` before returning: the body declares the chain and does no work.

## See also

- [Train Methods](/docs/sdk-reference/train-methods), every step a chain can take
- [Junctions](/docs/sdk-reference/train-methods/junctions), the override a chain is declared in
