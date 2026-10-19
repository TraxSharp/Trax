---
title: AddTrax
description: "Reference for AddTrax, the single entry point that registers Trax on an IServiceCollection, the TraxBuilder it hands out, and the services it registers."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 1
package: Trax.Effect
namespace:
  - Trax.Effect.Extensions
  - Trax.Effect.Configuration.TraxBuilder
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxBuilder/TraxBuilder.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxBuilder/TraxMarker.cs
---

# AddTrax

Registers Trax on the service collection. The `configure` callback receives a `TraxBuilder`, on which the
subsystems are added in a fixed order: [AddEffects](/docs/sdk-reference/configuration/add-effects), then
[AddMediator](/docs/sdk-reference/configuration/add-mediator), then
[AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler). Every Trax host calls it once.

## Signature

```csharp fragment
public static IServiceCollection AddTrax(
    this IServiceCollection services,
    Action<TraxBuilder> configure
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `services` | `IServiceCollection` | The host's service collection |
| `configure` | `Action<TraxBuilder>` | Adds the subsystems and sets the host identity |

## Returns

`IServiceCollection`, the same collection, for chaining.

## Behavior

`configure` runs first. Then `AddTrax` sets the process-wide host identity, `TraxHostInfo.Current`, from
auto-detection and the [host tracking](/docs/sdk-reference/configuration/host-tracking) methods called on the
builder, and registers the services below. When `configure` does not call `AddEffects`, the effect
configuration takes its defaults.

### TraxBuilder

| Member | Description |
|--------|-------------|
| `AddEffects(...)` | The first step: configures effects and data providers. Returns `TraxBuilderWithEffects`. See [AddEffects](/docs/sdk-reference/configuration/add-effects). |
| `SetHostEnvironment`, `SetHostInstanceId`, `AddHostLabel`, `AddHostLabels` | Override or extend the host identity. See [Host Tracking](/docs/sdk-reference/configuration/host-tracking). |
| `HasDataProvider` | True once `UsePostgres`, `UseSqlite` or `UseInMemory` ran. Set only by those methods. |
| `HasDatabaseProvider` | True once `UsePostgres` or `UseSqlite` ran; false for `UseInMemory`. Set only by those methods. |
| `ContributedMediatorAssemblies` | Assemblies a subsystem adds for `AddMediator` to scan, such as the state machine mutations' assembly `AddStateMachines` adds. `AddMediator` merges them into its scan. |
| `MediatorConfigured` | True once `AddMediator` has built the train registry. |
| `ServiceCollection` | The service collection being configured. |

### Order of the steps

Each step returns a builder type that has only the next steps on it, so an out-of-order chain does not
compile.

| Type | Returned by | Next step |
|------|-------------|-----------|
| `TraxBuilder` | the `AddTrax` callback | `AddEffects()` |
| `TraxBuilderWithEffects` | `AddEffects()` | `AddMediator()` |
| `TraxBuilderWithMediator` | `AddMediator()`, `AddScheduler()` | `AddScheduler()` |

A call on the wrong step binds to an overload marked obsolete as an error, so the compiler reports the
order (CS0619) instead of a missing method:

| Call | Error |
|------|-------|
| `AddMediator` before `AddEffects` | `Call AddEffects(...) before AddMediator(...).` |
| `AddMediator` a second time | `AddMediator(...) is already called. Call it once and configure everything in that call.` |
| `AddScheduler` before `AddMediator` | `Call AddMediator(...) before AddScheduler(...).` |

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `TraxMarker` | Singleton | Records that `AddTrax` ran. `AddTraxGraphQL()` throws `InvalidOperationException` without it: `AddTraxGraphQL() requires AddTrax() to be called first.` |
| `IEffectRegistry` | Singleton | The `EffectRegistry` every effect, junction effect and lifecycle hook is registered in, and toggled through |
| `ITraxEffectConfiguration` | Singleton | The effect settings `AddEffects` built |
| `IEffectRunner`, `IJunctionEffectRunner`, `ILifecycleHookRunner` | Transient | Run a train's effects, junction effects and lifecycle hooks |
| `ILifecycleHookOutputPolicy` | Singleton | `DefaultLifecycleHookOutputPolicy`, unless `SaveTrainParameters` registered its own first |
| `ITraxChangeSignal`, `TraxChangeSignal` | Singleton | Collects the data-change signals behind `onDataChanged`. Its meter counts dropped signals as `TraxChangeSignal.DroppedCounterName` (`trax.change_signal.dropped`). |
| `ChangeSignalCoalescer` | Hosted service | Flushes the collected signals to every registered `IChangeSignalSink` (`FlushAsync`), one per changed domain per window |
| `ChangeSignalOptions` | Singleton, if none is registered | `CoalesceWindow` (250 ms) and `ChannelCapacity` (1024). Register a configured instance before `AddTrax` to change them. |
| `TimeProvider` | Singleton, if none is registered | `TimeProvider.System` |
| `CheckpointOptions`, `IRunResumes`, `ICheckpointStore` | Singleton, if none is registered | The [checkpoint](/docs/sdk-reference/train-methods/checkpoint) store and resume path |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJson()
        .SaveTrainParameters()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
    .AddScheduler()
);
```

## See also

- [AddEffects](/docs/sdk-reference/configuration/add-effects), [AddMediator](/docs/sdk-reference/configuration/add-mediator), [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
- [Host Tracking](/docs/sdk-reference/configuration/host-tracking)
- [Builder pattern](/docs/contributing/api-conventions#builder-pattern), the convention the step builders follow
