---
layout: default
title: Train
description: "Reference for Train, the base class of every train with no persistence or container, for using Trax.Core on its own: type parameters and members."
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 1
---

# Train

The base class of every train. A train takes an input, runs the junctions its `Junctions()` override declares, and produces a result or an exception. It has no persistence, no lifecycle hooks and no container: those come with [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), which derives from it and is what most applications use.

Derive from `Train` directly only when you use Trax.Core on its own, without Trax.Effect.

## Signature

```csharp
namespace Trax.Core.Train;

public abstract class Train<TInput, TReturn> : IRoute<TInput, TReturn>
{
    protected Train();

    public string ExternalId { get; set; }
    [JsonIgnore]
    public CancellationToken CancellationToken { get; protected set; }
    public bool IsDeclaringChain { get; }

    public virtual Task<TReturn> Run(TInput input, CancellationToken cancellationToken = default);
    public Task<Either<Exception, TReturn>> RunEither(TInput input);
    public ChainRecorder DeclaredChain();

    protected virtual Task<Either<Exception, TReturn>> Junctions();

    // Chain methods, called inside Junctions()
    protected MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class;
    protected MonadTask<TInput, TReturn> IChain<TJunction>() where TJunction : class;
    protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class;
    protected Monad<TInput, TReturn> Extract<TIn, TOut>();
    protected Monad<TInput, TReturn> AddServices<T1>(T1 service);  // up to seven services
    protected Either<Exception, TReturn> Resolve();
    // ...plus the instance and explicit-type overloads listed on each method's page
}
```

`IRoute<TInput, TReturn>` is the one-method contract (`Run(input, cancellationToken)`) that trains implement; see [IRoute](/docs/sdk-reference/trains-and-junctions/i-route).

## Type Parameters

| Type Parameter | Description |
|----------------|-------------|
| `TInput` | The train's input. It is the first value in Memory, so the first junction can take it. |
| `TReturn` | The train's result. `Resolve()` takes it out of Memory at the end of the chain. |

## Members

| Member | Description |
|--------|-------------|
| `ExternalId` | Identifies one run across logs, stored metadata and failure data (`TrainExceptionData.TrainExternalId`). A new GUID in 32-digit `"N"` format when the train is constructed; set it before `Run` to correlate the run with an id you already have. |
| `CancellationToken` | The token of the current run, set by `Run` and copied to every junction before it runs. Not serialized. A caller cannot set it; pass the token to `Run`. |
| `IsDeclaringChain` | True while [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) is reading the chain rather than running it. |
| [Run / RunEither](/docs/sdk-reference/train-methods/run) | Runs the train. `Run` throws the failure, `RunEither` returns it as `Left`. `Run` is virtual here and sealed on `ServiceTrain`. |
| [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | Reads the declared chain without resolving or running a junction. The startup chain check calls it. |
| [Junctions](/docs/sdk-reference/train-methods/junctions) | The override that declares the chain. The base implementation throws `NotImplementedException`. |
| [Chain](/docs/sdk-reference/train-methods/chain), `IChain`, [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit), [Extract](/docs/sdk-reference/train-methods/extract), [AddServices](/docs/sdk-reference/train-methods/add-services), [Resolve](/docs/sdk-reference/train-methods/resolve) | The chain methods. All are `protected`: they exist to be called inside `Junctions()`. |

`NewMonad()` is also `protected virtual`, but it is the seam `ServiceTrain` uses to supply its container and is hidden from completion. It is not an extension point.

## Example

```csharp
using Trax.Core.Functional;
using Trax.Core.Train;

public class ScoreTrain : Train<ScoreInput, ScoreResult>
{
    protected override Task<Either<Exception, ScoreResult>> Junctions() =>
        Chain<NormalizeScore>()
            .Chain<RankScore>()
            .Resolve();
}

var result = await new ScoreTrain().Run(new ScoreInput(42));
```

The train constructs each junction itself, filling its constructor's parameters from Memory: the input, the outputs of earlier junctions, and services added with `AddServices`. A plain `Train` has no container, so a parameter Memory cannot supply fails the chain. A `ServiceTrain` also falls back to its container.

## Remarks

- **Overriding `Run`.** `Run` stays virtual so code built on Trax.Core alone can wrap it. An override that does not call `base.Run` runs something other than the declared chain, so `DeclaredChain()`, and any check built on it, no longer describes what runs (Trax.Core ADR 0003). A `ServiceTrain` cannot do this.
- **One run at a time.** A train instance carries the state of the run in progress. Do not run the same instance from two callers at once.
- **Nothing escapes `RunEither`.** A junction's exception, including cancellation, comes back as `Left`.

See [Trains and Junctions](/docs/core/trains-and-junctions) for the concepts.

## Package

```
dotnet add package Trax.Core
```
