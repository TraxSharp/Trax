---
title: AddMediator
description: "Reference for AddMediator: its overloads, TraxMediatorBuilder, assembly scanning, train lifetimes, the startup chain check and what it registers."
type: reference
area: mediator
parent: Configuration
grand_parent: SDK Reference
nav_order: 4
package: Trax.Mediator
namespace:
  - Trax.Mediator.Extensions
  - Trax.Mediator.Configuration
source:
  - Trax.Mediator/src/Trax.Mediator/Extensions/ServiceExtensions.cs
  - Trax.Mediator/src/Trax.Mediator/Configuration/TraxMediatorBuilder/TraxMediatorBuilder.cs
  - Trax.Mediator/src/Trax.Mediator/Configuration/TraxBuilderWithMediator.cs
---

# AddMediator

The second step on the [AddTrax](/docs/sdk-reference/configuration/add-trax) builder. Discovers every
`IServiceTrain<,>` implementation in the given assemblies, registers them, and registers `ITrainBus` and
`ITrainRegistry`, which dispatch a train by its input type or its name.

## Signatures

```csharp fragment
public static TraxBuilderWithMediator AddMediator(
    this TraxBuilderWithEffects builder,
    Func<TraxMediatorBuilder, TraxMediatorBuilder> configure
)

public static TraxBuilderWithMediator AddMediator(
    this TraxBuilderWithEffects builder,
    params Assembly[] assemblies
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Func<TraxMediatorBuilder, TraxMediatorBuilder>` | Configures scanning, lifetime and limits |
| `assemblies` | `Assembly[]` | The assemblies to scan, with every other setting at its default. The same as `AddMediator(m => m.ScanAssemblies(assemblies))`. |

### TraxMediatorBuilder

| Method | Default | Description |
|--------|---------|-------------|
| `ScanAssemblies(params Assembly[])` | | Adds assemblies to scan. With none, `AddMediator` throws `InvalidOperationException`. |
| `TrainLifetime(ServiceLifetime)` | `Transient` | The lifetime of discovered trains: `Transient` or `Scoped`. `Singleton` throws `ArgumentException` naming `TrainLifetime`, because a train instance carries the state of the run in progress; `RegisterServiceTrains` and `AddServiceTrainBus` refuse it the same way, and `AddSingletonTraxRoute` refuses a service train with `InvalidOperationException`. |
| `SkipChainVerification()` | off | Turns off the startup chain check, including its check of each train's `[Inject]` properties, and logs a warning at startup naming both. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic). |
| `GlobalConcurrentRunLimit(int)` | none | The most RUN executions at once across all trains; more wait for a slot. Throws `ArgumentOutOfRangeException` below 1. |
| `ConcurrentRunLimit<TTrain>(int)` | none | The same, for one train interface. Overrides a `[TraxConcurrencyLimit]` on the train. Throws below 1. |
| `PerPrincipalMaxConcurrentRun(int)` | none | The most RUN executions at once for one authenticated principal (the `trax:principal-id` claim). Applies to calls with an `HttpContext`; scheduler, worker and trusted-scope calls are not counted. Throws when not positive. |
| `WithMaxInputJsonBytes(int)` | 262144 (256 KiB) | The largest input JSON `ITrainExecutionService.RunAsync` and `QueueAsync` accept; larger is refused with `TrainInputValidationException` before it is deserialized. Throws when not positive. |
| `WithMaxQueueHookDuration(TimeSpan)` | 30 seconds | How long an `OnQueue` hook may run while its enqueue holds a connection with a transaction open. Past it the hook's token is cancelled and the enqueue fails with `QueueHookTimeoutException`, rolling back what the hook wrote. `Timeout.InfiniteTimeSpan` removes the limit. |
| `AllowMissingAuthorizationService()` | off | Lets the host start when trains carry `[TraxAuthorize]` and no `ITrainAuthorizationService` is registered. Those trains then run without an authorization check. For hosts that never serve API submissions. |

The built settings are the `MediatorConfiguration`; see [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration).

## Returns

`TraxBuilderWithMediator`, on which [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) is the next step.

## Behavior

Both overloads extend `TraxBuilderWithEffects`. Called before `AddEffects()`, `AddMediator` fails to compile with
`Call AddEffects(...) before AddMediator(...).`, and called a second time with
`AddMediator(...) is already called. Call it once and configure everything in that call.`. Assemblies a subsystem
added to `TraxBuilder.ContributedMediatorAssemblies` are scanned too.

### Registered services

1. Each discovered train, at the configured lifetime, under its service type
2. `ITrainBus`, which runs a train by input type or by name
3. `ITrainRegistry`, which maps input types and names to trains
4. Registers the startup chain validator, a hosted service that reads every registered train's `Junctions()` declaration when the host starts and refuses to start if any chain cannot run, reporting every failing train at once. It also checks that every junction a chain builds (`Chain<T>()`, `ShortCircuit<T>()`) will be handed its constructor arguments, from Memory as the chain has filled it by that step or from the container, and refuses a junction that would not. A train that can never be built is refused, naming the train: its class has no public constructor, or its constructor needs a type the container does not register, directly or through a registered dependency whose own constructor needs one (the check follows registrations that name an implementation class, open generics included, but not factories or instances). The train's own `[Inject]` properties are checked too, on the built train, so one its constructor or an initializer already sets is left alone: one that is null and whose type is not registered is refused unless the property is declared nullable, which logs a warning, and one whose registered type can never be built is refused. When the train cannot be built at startup at all, what this finds is logged as a warning instead. A junction built with `Chain<T>()` or `ShortCircuit<T>()` that declares `[Inject]` properties is logged as a warning, because those properties are never filled. A train it cannot build for another reason (a service registered through a factory that only works inside a request) or that does not derive from `Train<,>` is logged as a warning and skipped rather than refused. The check runs in `StartingAsync`, so it refuses the host before any hosted service's `StartAsync` runs, including under `HostOptions.ServicesStartConcurrently`; when something starts hosted services itself and calls only `StartAsync` (a custom `IHost`, a test harness), it runs from `StartAsync` instead. A train it can build whose `Junctions()` throws is refused, whatever the exception: a `Junctions()` that dereferences `Metadata`, which is null at startup, fails the start, reported as a train whose chain could not be read. Being a hosted service, the check runs only where hosted services start: not with `SkipChainVerification()`, and not on the Lambda runner or any other bare `ServiceProvider`, where such a train fails when it first runs instead. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)

### How Discovery Works

1. Scans the specified assemblies for all concrete classes implementing `IServiceTrain<TIn, TOut>`.
2. Selects each train's service type: its own interface, the non-generic one that derives from `IServiceTrain<TIn, TOut>`. When one train interface extends another, the most derived is selected. A train with no such interface is registered under the closed `IServiceTrain<TIn, TOut>`. No other interface counts, so a marker interface or `IDisposable` on a shared base class never becomes a train's service type. A train that implements two train interfaces, neither extending the other, has no single canonical name and is refused with a `TrainException`. `RegisterServiceTrains` selects the same way.
3. Registers the train in the `ITrainRegistry` keyed by the `TIn` of its service type.
4. Registers the train in the DI container under that service type, with the specified lifetime.

### Input type uniqueness

`TrainBus.RunAsync` dispatches by input type, so it reaches **one** train per input type. If two trains accept the same `TIn`, the registry keeps the first one scanned (via `TryAdd`), and `RunAsync` with that input type runs it.

Both trains are still registered and discovered, and a call that names a train runs that train, whichever one the registry keeps: `ITrainExecutionService.RunAsync`, the GraphQL `run` operations and `ITrainBus.RunByNameAsync` all do.

```csharp fragment
// This is fine -- different input types
public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult> { }
public class CancelOrderTrain : ServiceTrain<CancelOrderInput, OrderResult> { }

// trainBus.RunAsync(new OrderInput()) runs CreateOrderTrain; run UpdateOrderTrain by name
public class CreateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
public class UpdateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
```

`ITrainBus.RunByNameAsync` with a train interface's full name, or injecting the train by its interface, reaches any one of several trains that share an input type.

## Examples

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

With a scoped lifetime, two assemblies and a concurrency limit:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(IShipOrderTrain).Assembly, typeof(Program).Assembly)
        .TrainLifetime(ServiceLifetime.Scoped)
        .ConcurrentRunLimit<IRunAuditTrain>(2)
    )
);
```

## See also

- [TrainBus](/docs/sdk-reference/mediator-api/i-train-bus), [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration)
- [AddEffects](/docs/sdk-reference/configuration/add-effects), [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
