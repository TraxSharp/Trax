---
title: AddLifecycleHook
description: "Reference for AddLifecycleHook, ITrainLifecycleHook and ITrainLifecycleHookFactory: when each event fires, the input and output a hook sees, and its errors."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 31
package: Trax.Effect
namespace:
  - Trax.Effect.Extensions
  - Trax.Effect.Services.TrainLifecycleHook
  - Trax.Effect.Services.TrainLifecycleHookFactory
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect/Services/TrainLifecycleHook/ITrainLifecycleHook.cs
  - Trax.Effect/src/Trax.Effect/Services/TrainLifecycleHookFactory/ITrainLifecycleHookFactory.cs
  - Trax.Effect/src/Trax.Effect/Services/TrainLifecycleHookFactory/LifecycleHookFactory.cs
---

# AddLifecycleHook

Registers a lifecycle hook: code that runs when any train starts, completes, fails or is cancelled, outside the
train's own logic.

## Signatures

```csharp fragment
public static TraxEffectBuilder AddLifecycleHook<T>(
    this TraxEffectBuilder builder,
    bool toggleable = true
)
    where T : class

public static TraxEffectBuilder AddLifecycleHook<TLifecycleHookFactory>(
    this TraxEffectBuilder builder,
    TLifecycleHookFactory factory,
    bool toggleable = true
)
    where TLifecycleHookFactory : class, ITrainLifecycleHookFactory

public static TraxEffectBuilder AddLifecycleHook<TILifecycleHookFactory, TLifecycleHookFactory>(
    this TraxEffectBuilder builder,
    TLifecycleHookFactory factory,
    bool toggleable = true
)
    where TILifecycleHookFactory : class, ITrainLifecycleHookFactory
    where TLifecycleHookFactory : class, TILifecycleHookFactory
```

```csharp fragment
public interface ITrainLifecycleHook
{
    Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) => Task.CompletedTask;
    Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnStateChanged(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
}

public interface ITrainLifecycleHookFactory
{
    ITrainLifecycleHook Create();
    ITrainLifecycleHook Create(IServiceProvider serviceProvider) => Create();
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `T` | type parameter | | An `ITrainLifecycleHook` or an `ITrainLifecycleHookFactory`. Any other type throws `InvalidOperationException`. |
| `factory` | the factory type | | A factory instance to register |
| `toggleable` | `bool` | `true` | Whether the effect registry, and the dashboard, may turn the hook off at runtime |

## Returns

`TraxEffectBuilder`, for chaining.

## Behavior

`AddLifecycleHook<T>()` with a hook type registers the hook as transient and a `LifecycleHookFactory<T>` as its
singleton factory, so no factory class is needed. With a factory type it registers the factory itself. The hook's
constructor dependencies come from the scope the run executes in, so a scoped service such as `IDataContext` is the
run's own.

### Events

All five methods have default implementations, so a hook overrides only the ones it needs.

| Method | Fires |
|--------|-------|
| `OnStarted` | After the run's metadata row is saved, before its junctions run |
| `OnCompleted` | After a successful run's outcome is saved |
| `OnFailed` | After a failed run's outcome is saved. Includes an `OperationCanceledException` nothing asked for, such as an `HttpClient` timeout. |
| `OnCancelled` | After a requested cancellation (the run's token was cancelled or its cancel flag was set) is saved |
| `OnStateChanged` | After each of the four above, once that event has run on every hook. A hook that implements both sees each transition twice. |

A run the scheduler ends with no train running fires the same events, once, after its row is saved: `OnFailed` for
a run reaped as stale, failed on startup recovery, or failed at dispatch or submit, and `OnCancelled` for a run
cancelled before it started. The exception `OnFailed` receives carries the failure recorded on the row, such as
`StalePendingTimeout`.

### Input and output

| Event | `metadata.GetInput<T>()` | `metadata.GetOutput<T>()` |
|-------|------------------------|-------------------------|
| `OnStarted` | Yes | No |
| `OnCompleted` | Yes | Yes |
| `OnFailed` | Yes | No |
| `OnCancelled` | Yes | No |
| `OnStateChanged` | Yes | After a completion only |

Each returns `default` when the value is unavailable or not of the requested type, and for a run the scheduler
ended with no train running.

In `OnCompleted`, `metadata.Output` is the output as JSON whether or not
[SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) is configured: the stored copy, or
one serialized in memory for the hooks and not persisted. It follows the stored copy's rules, decided by the
registered `ILifecycleHookOutputPolicy`: `null` for an output the parameter effect skips, bounded by
`MaxParameterBytes` (1 MiB when it is `null` or `SaveTrainParameters` is not configured), past which it is
`{"_truncated": true, "_maxBytes": N}`. `GetOutput<T>()` returns the object itself. The state machine's internal
trains (`ISaveSnapshot`, `IAdvanceSnapshot`, `ILoadSnapshot`, `ISendSnapshot`, `IStartSnapshot`, `IDiscardSnapshot`)
give no hook their output: `metadata.Output` and `GetOutput<T>()` are `null`, because that output is an instance's
snapshot.

### Factories

The factory is a singleton and builds a hook per run. Trax calls `Create(IServiceProvider)` with the run's scope;
the default implementation calls `Create()`, which builds from whatever the factory captured, so a factory that
resolves scoped services overrides `Create(IServiceProvider)` and resolves from the provider it is given.
`LifecycleHookFactory<THook>` does: `Create(scopeProvider)` builds the hook from the run's scope with
`ActivatorUtilities`, and `Create()` from the root container.

The run owns the hooks its factories return: when it ends, each one that is `IDisposable` is disposed. A factory
that returns one shared instance to every run returns something that is not disposable, such as a forwarder to it.

### Errors

A hook's exception is caught and logged. It never fails the train, and the other hooks still run.

### Per-train hooks

`ServiceTrain<TIn, TOut>` has `protected virtual` `OnStarted`, `OnCompleted`, `OnFailed` and `OnCancelled` with the
same parameters, which a train overrides without registering anything. They run after the registered hooks for the
same event, and their exceptions are caught and logged the same way. A train also reads its input and output as
`TrainInput` and `TrainOutput`, typed by its generic arguments.

### Built-in hooks

| Hook | Package | Registered by |
|------|---------|---------------|
| `GraphQLSubscriptionHook` | `Trax.Api.GraphQL` | `AddTraxGraphQL()`. Publishes to [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions). |
| The broadcast hook (internal) | `Trax.Effect` | [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster). Publishes to the transport. |
| `SignalRTrainEventDispatcher` | `Trax.Effect.Broadcaster.SignalR` | [`UseSignalRHub()`](/docs/sdk-reference/configuration/use-signalr-hub). Pushes to connected SignalR clients. |

## Example

```csharp compile
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddLifecycleHook<FailureLogHook>()
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);

public sealed class FailureLogHook(ILogger<FailureLogHook> logger) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        logger.LogWarning("{Train} failed: {Message}", metadata.Name, exception.Message);
        return Task.CompletedTask;
    }
}
```

## See also

- [Write a lifecycle hook](/docs/effect/write-a-lifecycle-hook)
- [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters), [AddEffect](/docs/sdk-reference/configuration/add-effect)
