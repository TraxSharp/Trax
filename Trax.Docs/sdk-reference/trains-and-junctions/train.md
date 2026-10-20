---
title: Train
description: "Reference for Train, the base class of every train, with no persistence or container: its type parameters, members and run behaviour."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Train
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 1
---

# Train

The base class of every train. A train takes an input, runs the junctions its `Junctions()` override declares, and produces a result or an exception. It has no persistence, no lifecycle hooks and no container: those come with [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), which derives from it. A class that derives from `Train` directly uses Trax.Core without Trax.Effect.

## Signature

```csharp fragment
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

    // Chain methods, called inside Junctions(); each has its own page
    protected MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class;
    protected MonadTask<TInput, TReturn> IChain<TJunction>() where TJunction : class;
    protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class;
    protected Monad<TInput, TReturn> Extract<TIn, TOut>();
    protected Monad<TInput, TReturn> AddServices<T1>(T1 service);
    protected Either<Exception, TReturn> Resolve();
    protected MonadTask<TInput, TReturn> Decide<TState>(Func<Questions<TState>, Questions<TState>> questions);
    protected MonadTask<TInput, TReturn> Switch<TTrack>(Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks)
        where TTrack : struct, Enum;
    protected MonadTask<TInput, TReturn> Gate<TQuestion>(Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate);
    protected MonadTask<TInput, TReturn> Scale<TLevel>(Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale)
        where TLevel : struct, Enum;
    protected MonadTask<TInput, TReturn> Parallel(Func<Branches<TInput, TReturn>, Branches<TInput, TReturn>> branches);
    protected MonadTask<TInput, TReturn> Checkpoint<TState>();
    // ...plus the overloads listed on each method's page

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual Monad<TInput, TReturn> NewMonad();
}
```

## Type parameters

| Type parameter | Description |
|----------------|-------------|
| `TInput` | The train's input. It is the first value in Memory, so the first junction can take it. |
| `TReturn` | The train's result. `Resolve()` takes it out of Memory at the end of the chain. |

## Members

| Member | Description |
|--------|-------------|
| `ExternalId` | Identifies one run across logs, stored metadata and failure data (`TrainExceptionData.TrainExternalId`). A new GUID in 32-digit `"N"` format when the train is constructed; set it before `Run` to correlate the run with an id you already have. |
| `CancellationToken` | The token of the current run, set by `Run` and copied to every junction before it runs. Not serialized. A caller cannot set it; pass the token to `Run`. |
| `IsDeclaringChain` | True while [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) is reading the chain rather than running it. |
| [Run](/docs/sdk-reference/train-methods/run) | Runs the train and throws the failure. Virtual here, sealed on `ServiceTrain`. |
| [RunEither](/docs/sdk-reference/train-methods/run-either) | Runs the train and returns the failure as `Left`. |
| [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | Reads the declared chain without resolving or running a junction. The startup chain check calls it. |
| [Junctions](/docs/sdk-reference/train-methods/junctions) | The override that declares the chain. The base implementation throws `NotImplementedException`. |
| [Chain](/docs/sdk-reference/train-methods/chain), [IChain](/docs/sdk-reference/train-methods/i-chain), [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit), [Extract](/docs/sdk-reference/train-methods/extract), [AddServices](/docs/sdk-reference/train-methods/add-services), [Resolve](/docs/sdk-reference/train-methods/resolve) | The chain methods. All are `protected`: they are called inside `Junctions()`. |
| [Decide](/docs/sdk-reference/train-methods/decide), [Switch](/docs/sdk-reference/train-methods/switch), [Gate](/docs/sdk-reference/train-methods/gate), [Scale](/docs/sdk-reference/train-methods/scale) | The decision and routing steps, also `protected`. |
| [Parallel](/docs/sdk-reference/train-methods/parallel), [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | Branches that run side by side, and a stored state a later run resumes after. |
| `NewMonad()` | The seam `ServiceTrain` overrides to supply its container. Hidden from completion; not an extension point. |

## Behavior

- **Building junctions.** A plain `Train` constructs each junction itself, filling its constructor's parameters from Memory: the input, the outputs of earlier junctions, and services added with `AddServices`. It has no container, so a parameter Memory cannot supply fails the chain. A `ServiceTrain` also falls back to its container.
- **One run at a time.** An instance carries the state of the run in progress, so two concurrent `Run` calls on one instance share it.
- **Nothing escapes `RunEither`.** A junction's exception, including cancellation, comes back as `Left`, and so does an exception `Junctions()` itself throws.
- **Overriding `Run`.** `Run` is virtual so code built on Trax.Core alone can wrap it. An override that does not call `base.Run` never reaches `Junctions()`, so `DeclaredChain()`, and any check built on it, does not describe what runs (Trax.Core ADR 0003). `ServiceTrain` seals it.

A checkout whose card is always declined, from Trax.Core's tests. `RunEither` hands back the junction's own exception rather than throwing it:

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Failures/Checkout.cs
public class ChargeCard : Junction<Order, Payment>
{
    public override Task<Payment> Run(Order order) =>
        throw new InvalidOperationException("Card declined");
}
// ...
public class CheckoutTrain(IOutbox outbox) : Train<Order, Receipt>
{
    protected override Task<Either<Exception, Receipt>> Junctions() =>
        AddServices(outbox).Chain<ChargeCard>().Chain<SendReceipt>().Resolve();
}
```

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Failures/RunEitherTests.cs
    [Test]
    public async Task RunEither_ChargeCardThrows_ReturnsTheExceptionInsteadOfThrowing()
    {
        var result = await new CheckoutTrain(new Outbox()).RunEither(new Order(20m));

        result.IsLeft.Should().BeTrue();
        var message = result.Match(Right: receipt => "", Left: failure => failure.Message);
        message.Should().Be("Card declined");
    }
```

## Example

```csharp compile=score
using Trax.Core.Functional;
using Trax.Core.Train;

public class ScoreTrain : Train<ScoreInput, ScoreResult>
{
    protected override Task<Either<Exception, ScoreResult>> Junctions() =>
        Chain<NormalizeScore>().Chain<RankScore>().Resolve();
}
```

```csharp compile=score
var result = await new ScoreTrain().Run(new ScoreInput(42));
```

## See also

- [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), the base class applications derive from
- [Junction](/docs/sdk-reference/trains-and-junctions/junction) and [MonadTask](/docs/sdk-reference/trains-and-junctions/monad-task)
- [Train Methods](/docs/sdk-reference/train-methods)
- [Trains and Junctions](/docs/core/trains-and-junctions), how a train runs its chain
