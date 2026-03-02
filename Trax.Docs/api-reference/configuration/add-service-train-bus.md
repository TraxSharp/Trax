---
layout: default
title: AddServiceTrainBus
parent: Configuration
grand_parent: API Reference
nav_order: 7
---

# AddServiceTrainBus

Registers the `ITrainBus` and `ITrainRegistry` services, and discovers all `IServiceTrain<,>` implementations via assembly scanning. This enables dynamic train dispatch by input type.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddServiceTrainBus(
    this Trax.CoreEffectConfigurationBuilder configurationBuilder,
    ServiceLifetime effectTrainServiceLifetime = ServiceLifetime.Transient,
    params Assembly[] assemblies
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `effectTrainServiceLifetime` | `ServiceLifetime` | No | `Transient` | The DI lifetime for discovered train registrations |
| `assemblies` | `params Assembly[]` | Yes | — | Assemblies to scan for `IServiceTrain<,>` implementations |

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(
        effectTrainServiceLifetime: ServiceLifetime.Scoped,
        assemblies: typeof(Program).Assembly)
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

Each input type must map to **exactly one** train. If two trains accept the same `TIn`, registration will throw an exception.

```csharp
// This is fine — different input types
public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult> { }
public class CancelOrderTrain : ServiceTrain<CancelOrderInput, OrderResult> { }

// This will FAIL — both accept OrderInput
public class CreateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
public class UpdateOrderTrain : ServiceTrain<OrderInput, OrderResult> { }
```

## Lifetime Considerations

| Lifetime | When to Use |
|----------|-------------|
| `Transient` (default) | Most trains — each execution gets a fresh instance |
| `Scoped` | When the train needs to share state with other scoped services in the same request |
| `Singleton` | Rarely appropriate — trains typically have per-execution state |

## Remarks

- The assembly scanning uses reflection to find `IServiceTrain<,>` implementations. Ensure the assemblies containing your trains are passed to the `assemblies` parameter.
- Trains registered here are available both through `ITrainBus.RunAsync` and through the scheduler system.
- See [TrainBus]({{ site.baseurl }}{% link api-reference/mediator-api/train-bus.md %}) for the runtime dispatch API.

## Package

Part of `Trax.Mediator`.
