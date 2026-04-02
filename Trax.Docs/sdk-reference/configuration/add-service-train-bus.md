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
