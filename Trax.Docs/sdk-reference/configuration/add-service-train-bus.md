---
layout: default
title: AddMediator
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

Both overloads are called on `TraxBuilderWithEffects` (the return type of `AddEffects()`), which enforces at compile time that effects are configured before the mediator. Both return `TraxBuilderWithMediator`, which exposes `AddScheduler()` as the next valid step.

## TraxMediatorBuilder

The builder overload passes a `TraxMediatorBuilder` with the following methods:

| Method | Returns | Description |
|--------|---------|-------------|
| `ScanAssemblies(params Assembly[])` | `TraxMediatorBuilder` | Adds assemblies to scan for `IServiceTrain<,>` implementations |
| `TrainLifetime(ServiceLifetime)` | `TraxMediatorBuilder` | Sets the DI lifetime for discovered train registrations (default: `Transient`) |
| `SkipChainVerification()` | `TraxMediatorBuilder` | Turns off the startup chain check. Logs a warning at startup instead. Use it for the check's blind spot (a junction asking for an interface only a subtype of the declared input implements), or temporarily while migrating a codebase whose chains do not pass yet. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) |

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
5. Registers the startup chain validator, a hosted service that reads every registered train's `Junctions()` declaration when the host starts and refuses to start if any chain cannot run, reporting every failing train at once. A train it cannot build (its constructor needs something only a request provides) or that does not derive from `Train<,>` is logged as a warning and skipped rather than refused. A train it can build whose `Junctions()` throws is refused, whatever the exception: a `Junctions()` that dereferences `Metadata`, which is null at startup, fails the start, reported as a train whose chain could not be read. Being a hosted service, the check runs only where hosted services start: not with `SkipChainVerification()`, and not on the Lambda runner or any other bare `ServiceProvider`, where such a train fails when it first runs instead. See [Trains & Junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)

## How Discovery Works

1. Scans the specified assemblies for all types implementing `IServiceTrain<TIn, TOut>`.
2. For each discovered type, extracts the `TIn` (input) type.
3. Registers the train in the `ITrainRegistry` keyed by `TIn`.
4. Registers the train in the DI container with the specified lifetime.

## Input Type Uniqueness

Each input type maps to **exactly one** train via the `TrainBus`. If two trains accept the same `TIn`, the first registration wins and the duplicate is silently skipped via `TryAdd`. Only the first-registered train will be dispatched when calling `RunAsync` with that input type.

```csharp
// This is fine -- different input types
public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult> { }
public class CancelOrderTrain : ServiceTrain<CancelOrderInput, OrderResult> { }

// Only CreateOrderTrain will be dispatched via TrainBus -- UpdateOrderTrain is silently skipped
public class CreateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
public class UpdateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
```

If you need multiple trains that share an input type, inject them directly by interface instead of dispatching through the bus.

## Lifetime Considerations

| Lifetime | When to Use |
|----------|-------------|
| `Transient` (default) | Most trains -- each execution gets a fresh instance |
| `Scoped` | When the train needs to share state with other scoped services in the same request |
| `Singleton` | Rarely appropriate -- trains typically have per-execution state |

## Remarks

- The assembly scanning uses reflection to find `IServiceTrain<,>` implementations. The assemblies containing your trains must be passed to `ScanAssemblies()` or the shorthand overload.
- Trains registered here are available both through `ITrainBus.RunAsync` and through the scheduler system.
- See [TrainBus](/docs/sdk-reference/mediator-api/train-bus) for the runtime dispatch API.

## Package

Part of `Trax.Mediator`.
