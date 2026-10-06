---
layout: default
title: AddMediator
description: "Reference for AddMediator: the builder and shorthand overloads, TraxMediatorBuilder, assembly scanning, train lifetimes and what it registers."
parent: Configuration
grand_parent: SDK Reference
nav_order: 7
---

# AddMediator

Registers the `ITrainBus` and `ITrainRegistry` services, and discovers all `IServiceTrain<,>` implementations via assembly scanning. This enables dynamic train dispatch by input type.

## Signatures

There are two overloads: a **builder overload** for full control, and a **shorthand overload** for the common case.

### Builder Overload

```csharp
public static TraxBuilderWithMediator AddMediator(
    this TraxBuilderWithEffects builder,
    Func<TraxMediatorBuilder, TraxMediatorBuilder> configure
)
```

Accepts a lambda that receives a `TraxMediatorBuilder` for configuring assembly scanning and train lifetime.

### Shorthand Overload

```csharp
public static TraxBuilderWithMediator AddMediator(
    this TraxBuilderWithEffects builder,
    params Assembly[] assemblies
)
```

Scans the given assemblies with the default `Transient` lifetime. Equivalent to:

```csharp
.AddMediator(mediator => mediator
    .ScanAssemblies(typeof(Program).Assembly)
)
```

Both overloads are called on `TraxBuilderWithEffects` (the return type of `AddEffects()`), which enforces at compile time that effects are configured before the mediator. Both return `TraxBuilderWithMediator`, which exposes `AddScheduler()` as the next valid step. Called before `AddEffects()`, `AddMediator` fails to compile with `Call AddEffects(...) before AddMediator(...).`, and called a second time with `AddMediator(...) is already called. Call it once and configure everything in that call.`

## TraxMediatorBuilder

The builder overload passes a `TraxMediatorBuilder` with the following methods:

| Method | Returns | Description |
|--------|---------|-------------|
| `ScanAssemblies(params Assembly[])` | `TraxMediatorBuilder` | Adds assemblies to scan for `IServiceTrain<,>` implementations |
| `TrainLifetime(ServiceLifetime)` | `TraxMediatorBuilder` | Sets the DI lifetime for discovered train registrations: `Transient` (the default) or `Scoped`. `Singleton` throws an `ArgumentException` naming `TrainLifetime`, because a train instance carries the state of the run in progress and one shared instance would mix concurrent runs together. `RegisterServiceTrains` and `AddServiceTrainBus` refuse it the same way |
| `SkipChainVerification()` | `TraxMediatorBuilder` | Turns off the startup chain check, including its check of each train's `[Inject]` properties. Logs a warning at startup instead, which names both. Use it for the check's blind spot (a junction asking for an interface only a subtype of the declared input implements), or temporarily while migrating a codebase whose chains do not pass yet. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) |

## Returns

`TraxBuilderWithMediator` -- enables `AddScheduler()` as the next step in the fluent chain.

## Examples

### Shorthand (Most Common)

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
);
```

### Builder with Custom Lifetime

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .TrainLifetime(ServiceLifetime.Scoped)
    )
);
```

### Multiple Assemblies

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(mediator => mediator
        .ScanAssemblies(
            typeof(Program).Assembly,
            typeof(SharedTrains).Assembly
        )
    )
);
```

## What It Registers

1. Scans the specified assemblies for all types implementing `IServiceTrain<TIn, TOut>`
2. Registers each discovered train with the DI container at the specified lifetime
3. Registers `ITrainBus` for dynamic train dispatch
4. Registers `ITrainRegistry` for train type lookup
5. Registers the startup chain validator, a hosted service that reads every registered train's `Junctions()` declaration when the host starts and refuses to start if any chain cannot run, reporting every failing train at once. It also checks that every junction a chain builds (`Chain<T>()`, `ShortCircuit<T>()`) will be handed its constructor arguments, from Memory as the chain has filled it by that step or from the container, and refuses a junction that would not. A train that can never be built is refused, naming the train: its class has no public constructor, or its constructor needs a type the container does not register, directly or through a registered dependency whose own constructor needs one (the check follows registrations that name an implementation class, open generics included, but not factories or instances). The train's own `[Inject]` properties are checked too, on the built train, so one its constructor or an initializer already sets is left alone: one that is null and whose type is not registered is refused unless the property is declared nullable, which logs a warning, and one whose registered type can never be built is refused. When the train cannot be built at startup at all, what this finds is logged as a warning instead. A junction built with `Chain<T>()` or `ShortCircuit<T>()` that declares `[Inject]` properties is logged as a warning, because those properties are never filled. A train it cannot build for another reason (a service registered through a factory that only works inside a request) or that does not derive from `Train<,>` is logged as a warning and skipped rather than refused. The check runs in `StartingAsync`, so it refuses the host before any hosted service's `StartAsync` runs, including under `HostOptions.ServicesStartConcurrently`; when something starts hosted services itself and calls only `StartAsync` (a custom `IHost`, a test harness), it runs from `StartAsync` instead. A train it can build whose `Junctions()` throws is refused, whatever the exception: a `Junctions()` that dereferences `Metadata`, which is null at startup, fails the start, reported as a train whose chain could not be read. Being a hosted service, the check runs only where hosted services start: not with `SkipChainVerification()`, and not on the Lambda runner or any other bare `ServiceProvider`, where such a train fails when it first runs instead. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)

## How Discovery Works

1. Scans the specified assemblies for all concrete classes implementing `IServiceTrain<TIn, TOut>`.
2. Selects each train's service type: its own interface, the non-generic one that derives from `IServiceTrain<TIn, TOut>`. When one train interface extends another, the most derived is selected. A train with no such interface is registered under the closed `IServiceTrain<TIn, TOut>`. No other interface counts, so a marker interface or `IDisposable` on a shared base class never becomes a train's service type. A train that implements two train interfaces, neither extending the other, has no single canonical name and is refused with a `TrainException`. `RegisterServiceTrains` selects the same way.
3. Registers the train in the `ITrainRegistry` keyed by the `TIn` of its service type.
4. Registers the train in the DI container under that service type, with the specified lifetime.

## Input Type Uniqueness

`TrainBus.RunAsync` dispatches by input type, so it reaches **one** train per input type. If two trains accept the same `TIn`, the registry keeps the first one scanned (via `TryAdd`), and `RunAsync` with that input type runs it.

Both trains are still registered and discovered, and a call that names a train runs that train, whichever one the registry keeps: `ITrainExecutionService.RunAsync`, the GraphQL `run` operations and `ITrainBus.RunByNameAsync` all do.

```csharp
// This is fine -- different input types
public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult> { }
public class CancelOrderTrain : ServiceTrain<CancelOrderInput, OrderResult> { }

// trainBus.RunAsync(new OrderInput()) runs CreateOrderTrain; run UpdateOrderTrain by name
public class CreateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
public class UpdateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
```

To run a specific one of several trains that share an input type, call `ITrainBus.RunByNameAsync` with its interface's full name, or inject it by its interface.

## Lifetime Considerations

| Lifetime | When to Use |
|----------|-------------|
| `Transient` (default) | Most trains -- each execution gets a fresh instance |
| `Scoped` | When the train needs to share state with other scoped services in the same request |
| `Singleton` | Not supported: a train carries per-run state, and `AddSingletonTraxRoute` refuses a service train at registration with an `InvalidOperationException` |

## Remarks

- The assembly scanning uses reflection to find `IServiceTrain<,>` implementations. The assemblies containing your trains must be passed to `ScanAssemblies()` or the shorthand overload.
- Trains registered here are available both through `ITrainBus.RunAsync` and through the scheduler system.
- See [TrainBus](/docs/sdk-reference/mediator-api/train-bus) for the runtime dispatch API.

## Package

Part of `Trax.Mediator`.
