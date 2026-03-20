---
layout: default
title: Train Registration
parent: DI Registration
grand_parent: Cross-Cutting
nav_order: 1
---

# Train Registration

Extension methods for registering Trax.Core trains with the .NET DI container. These methods wrap standard `AddScoped`/`AddTransient`/`AddSingleton` and add `[Inject]` property injection support.

{: .sdk-references }
> [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus)

## Signatures

### Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddTransientTraxRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddSingletonTraxRoute<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService
```

### Non-Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxRoute(
    this IServiceCollection services,
    Type serviceInterface,
    Type serviceImplementation
)

public static IServiceCollection AddTransientTraxRoute(
    this IServiceCollection services,
    Type serviceInterface,
    Type serviceImplementation
)

public static IServiceCollection AddSingletonTraxRoute(
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
services.AddTransientTraxRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddScopedTraxRoute<IProcessPaymentTrain, ProcessPaymentTrain>();
```

## What It Does

1. Registers `TImplementation` with the DI container at the specified lifetime.
2. Registers `TService` with a factory that:
   - Resolves `TImplementation` from the container
   - Calls `InjectProperties()` to set all `[Inject]`-attributed properties on the instance
   - Sets `CanonicalName` to `TService`'s `FullName` (the interface name) — this is used as the train's canonical identifier in metadata, work queue entries, and subscription matching. See [Canonical Train Naming](/docs/effect/architecture#canonical-train-naming).
   - Returns the fully-initialized instance

## When to Use

Use these methods when your train class (or its base class) uses `[Inject]` properties. For example, `ServiceTrain<TIn, TOut>` has:

```csharp
[Inject] public IEffectRunner? EffectRunner { get; set; }
[Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
[Inject] public IJunctionEffectRunner? JunctionEffectRunner { get; set; }
```

Without `AddTraxRoute`, these properties would remain `null` after DI resolution.

## Remarks

- If your trains are discovered via [AddMediator](/docs/sdk-reference/configuration/add-service-train-bus), you don't need to register them manually — the bus handles registration automatically.
- These methods are primarily useful for trains registered outside of assembly scanning, or when you need explicit control over the DI lifetime.
- The non-generic overloads accept `Type` parameters for dynamic/reflection-based registration scenarios.
