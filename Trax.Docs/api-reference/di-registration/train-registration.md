---
layout: default
title: Train Registration
parent: DI Registration
grand_parent: API Reference
nav_order: 1
---

# Train Registration

Extension methods for registering Trax.Core trains with the .NET DI container. These methods wrap standard `AddScoped`/`AddTransient`/`AddSingleton` and add `[Inject]` property injection support.

## Signatures

### Generic Overloads

```csharp
public static IServiceCollection AddScopedTrax.CoreRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddTransientTrax.CoreRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddSingletonTrax.CoreRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService
```

### Non-Generic Overloads

```csharp
public static IServiceCollection AddScopedTrax.CoreRoute(
    this IServiceCollection services,
    Type serviceInterface,
    Type serviceImplementation
)

public static IServiceCollection AddTransientTrax.CoreRoute(
    this IServiceCollection services,
    Type serviceInterface,
    Type serviceImplementation
)

public static IServiceCollection AddSingletonTrax.CoreRoute(
    this IServiceCollection services,
    Type serviceInterface,
    Type serviceImplementation
)
```

## Type Parameters

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TService` | `class` | The service interface type (e.g., `IMyTrain`) |
| `TImplementation` | `class, TService` | The implementation type (e.g., `MyTrain`) |

## Returns

`IServiceCollection` — for continued chaining.

## Example

```csharp
services.AddTransientTrax.CoreRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddScopedTrax.CoreRoute<IProcessPaymentTrain, ProcessPaymentTrain>();
```

## What It Does

1. Registers `TImplementation` with the DI container at the specified lifetime.
2. Registers `TService` with a factory that:
   - Resolves `TImplementation` from the container
   - Calls `InjectProperties()` to set all `[Inject]`-attributed properties on the instance
   - Returns the fully-initialized instance

## When to Use

Use these methods when your train class (or its base class) uses `[Inject]` properties. For example, `ServiceTrain<TIn, TOut>` has:

```csharp
[Inject] public IEffectRunner? EffectRunner { get; set; }
[Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
[Inject] public IStepEffectRunner? StepEffectRunner { get; set; }
```

Without `AddTrax.CoreRoute`, these properties would remain `null` after DI resolution.

## Remarks

- If your trains are discovered via [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-train-bus.md %}), you don't need to register them manually — the bus handles registration automatically.
- These methods are primarily useful for trains registered outside of assembly scanning, or when you need explicit control over the DI lifetime.
- The non-generic overloads accept `Type` parameters for dynamic/reflection-based registration scenarios.
