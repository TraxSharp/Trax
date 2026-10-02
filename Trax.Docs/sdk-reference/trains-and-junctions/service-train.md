---
layout: default
title: ServiceTrain
description: "Reference for ServiceTrain, the train base class applications derive from: metadata per run, effects, lifecycle hooks, registration and disposal."
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 2
---

# ServiceTrain

The train base class applications derive from. It extends [Train](/docs/sdk-reference/trains-and-junctions/train) with a metadata row per run, effect providers, junction effects, lifecycle hooks, and a container its junctions are built from. The mediator, the scheduler and the GraphQL API run `ServiceTrain`s.

A service train implements its own interface, which derives from `IServiceTrain<TIn, TOut>`. That interface is how the train is registered, resolved, named and dispatched.

## Signature

```csharp
namespace Trax.Effect.Services.ServiceTrain;

public interface IServiceTrain<in TIn, TOut> : IRoute<TIn, TOut>, IDisposable
{
    Metadata? Metadata { get; }
    new Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
}

public abstract class ServiceTrain<TIn, TOut> : Train<TIn, TOut>, IServiceTrain<TIn, TOut>
{
    protected ServiceTrain();

    public Metadata? Metadata { get; }
    public string TrainName { get; }
    public string? CanonicalName { get; set; }
    public long? ParentId { get; }

    protected TIn TrainInput { get; }
    protected TOut TrainOutput { get; }

    public sealed override Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
    public Task<TOut> Run(TIn input, Metadata metadata);
    public Task<TOut> Run(TIn input, Metadata metadata, CancellationToken cancellationToken);
    public void Dispose();

    protected virtual Task OnStarted(Metadata metadata, CancellationToken ct);
    protected virtual Task OnCompleted(Metadata metadata, CancellationToken ct);
    protected virtual Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct);
    protected virtual Task OnCancelled(Metadata metadata, CancellationToken ct);
    protected virtual Task OnQueue(Metadata metadata, CancellationToken ct);
    protected virtual string? QueueSubjectKey(Metadata metadata);
    protected virtual bool DeferQueuePromotion { get; }

    // Filled by property injection; see "Registration" below.
    [Inject] public IEffectRunner? EffectRunner { get; set; }
    [Inject] public IJunctionEffectRunner? JunctionEffectRunner { get; set; }
    [Inject] public ILifecycleHookRunner? LifecycleHookRunner { get; set; }
    [Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
    [Inject] public IServiceProvider? ServiceProvider { get; set; }
}
```

Everything on [Train](/docs/sdk-reference/trains-and-junctions/train) (`Junctions()`, the chain methods, `ExternalId`, `CancellationToken`, `RunEither`, `DeclaredChain()`) is inherited unchanged. `NewMonad()` is sealed. `EnterQueueHooks(Metadata)` is public for the mediator's enqueue path and hidden from completion; consumers do not call it.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Metadata` | `Metadata?` | The row for the current or most recent run: state, input, output, timing, failure. `null` until a run starts. Not serialized. |
| `TrainName` | `string` | The canonical name: `CanonicalName` when set, otherwise the concrete type's `FullName`. Metadata, work queue entries and subscriptions use it. |
| `CanonicalName` | `string?` | The train interface's `FullName`, set by the [registration helpers](/docs/sdk-reference/trains-and-junctions/route-registration) when the train is resolved through its interface. Not serialized. |
| `ParentId` | `long?` | Copied onto each run's metadata row. No Trax package sets it, so it is `null`: a train dispatched from another train is recorded as a run of its own, not linked to the parent. |
| `TrainInput` | `TIn` | The run's typed input, available in `OnStarted`, `OnCompleted`, `OnFailed` and `OnCancelled`, and in `QueueSubjectKey` and `OnQueue` when the enqueue hands it over. Throws `ChainDeclarationException` while the chain is being read. |
| `TrainOutput` | `TOut` | The run's typed output. Meaningful only in `OnCompleted`; `default` elsewhere. Throws `ChainDeclarationException` while the chain is being read. |
| `EffectRunner`, `JunctionEffectRunner`, `LifecycleHookRunner`, `ServiceProvider` | | Framework services filled by property injection. `Run` throws when any of them is `null`. |
| `Logger` | `ILogger<ServiceTrain<TIn, TOut>>?` | Filled by property injection; optional. |

## Run

| Overload | Description |
|----------|-------------|
| `Run(input, cancellationToken)` | Creates a metadata row, runs the chain, records the outcome and fires the hooks. Sealed. Each call records its own row: running the same instance again starts a new row, with a new `ExternalId` unless you set one first. |
| `Run(input, metadata)` | Runs as a `Pending` row the caller already created, instead of creating one. Used by the mediator and the scheduler. Throws `TrainException` when the row is not `Pending`. Runs with the train's current `CancellationToken`. |
| `Run(input, metadata, cancellationToken)` | As above, with a token. |

`Run` throws the failure, as on `Train`. See [Run / RunEither](/docs/sdk-reference/train-methods/run).

## Lifecycle hooks

| Hook | When it runs | An exception thrown in it |
|------|--------------|---------------------------|
| `OnStarted` | After the row is persisted as in progress, before the chain runs | Is logged; the train still runs |
| `OnCompleted` | After a successful run, once the output is persisted and the registered lifecycle hooks have fired | Is logged; the run stays successful |
| `OnFailed` | After a failed run, once the failure is persisted and the registered hooks have fired. An `OperationCanceledException` nothing asked for, such as an `HttpClient` timeout, is a failure and lands here | Is logged; the original failure still propagates |
| `OnCancelled` | After a cancellation the run was asked for: its token was cancelled, or its persisted cancel flag was set | Is logged |
| `OnQueue` | At enqueue time, inside the mediator's queue path. Not on the run path, and not again when the queued run executes | Propagates and aborts the enqueue |
| `QueueSubjectKey` | At enqueue time. Return a key to stop two entries for the same subject being dispatched at once; `null` (the default) serializes nothing | Propagates and aborts the enqueue |
| `DeferQueuePromotion` | Read at enqueue time. `true` commits the entry unconfirmed and confirms it after `OnQueue` returns; no effect unless `OnQueue` is overridden | |

The enqueue-time hooks are covered in depth in [OnQueue](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) and [QueueSubjectKey](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing). For hooks that apply to every train, register an `ITrainLifecycleHook` with [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook).

## Registration

A service train is resolved through its interface, which is what fills the `[Inject]` properties and sets `CanonicalName`.

- [AddMediator](/docs/sdk-reference/configuration/add-mediator) scans the assemblies you give it and registers every non-abstract `IServiceTrain<,>` implementation under its own interface, transient by default.
- Outside the mediator, register it with [AddTransientTraxRoute or AddScopedTraxRoute](/docs/sdk-reference/trains-and-junctions/route-registration).

A service train cannot be a singleton: `AddSingletonTraxRoute` refuses one with an `InvalidOperationException`, and `AddMediator` refuses a singleton train lifetime with an `ArgumentException`. An instance carries the state of the run in progress, so it runs one execution at a time and is not shared between concurrent callers.

Resolving the concrete class instead of the interface skips the property injection, and `Run` then throws because the framework services are `null`.

## Example

```csharp
using LanguageExt;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

public interface ICreateOrderTrain : IServiceTrain<CreateOrderInput, OrderResult>;

public class CreateOrderTrain(IOrderAlerts alerts)
    : ServiceTrain<CreateOrderInput, OrderResult>, ICreateOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>()
            .Chain<ChargePayment>()
            .Chain<PersistOrder>()
            .Resolve();

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
        // TrainInput is the typed input of the run that failed.
        => alerts.OrderFailed(TrainInput.OrderId, exception.Message, ct);
}
```

The train's own dependencies, `IOrderAlerts` here, come through its constructor like any other service. The junctions take theirs through their own constructors.

## Dispose

`Dispose()` clears the in-memory input and output, disposes the effect, junction effect and lifecycle hook runners (and through them their providers) and the metadata, and drops the logger and service provider. The instance cannot run again afterwards. The container disposes a transient or scoped train for you.

See [Trains and Junctions](/docs/core/trains-and-junctions) and [Effect](/docs/effect) for the concepts.

## Package

```
dotnet add package Trax.Effect
```
