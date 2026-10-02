---
layout: default
title: AddServiceTrainBus
description: Reference for AddServiceTrainBus and RegisterServiceTrains, the registration AddMediator performs, and why it is not a standalone entry point.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 11
---

# AddServiceTrainBus

The `IServiceCollection` registration that [AddMediator](/docs/sdk-reference/configuration/add-mediator) performs: the train bus, the registry, discovery, execution, the concurrency limiter, the trusted scope, the startup checks, and every train found in the given assemblies. `RegisterServiceTrains` is the part of it that registers the trains alone.

Call `AddMediator` instead. `AddServiceTrainBus` does not register the `MediatorConfiguration` its own services and startup checks depend on, so a host built with it alone fails to resolve them. It is public, but it is not a standalone entry point.

## Signatures

```csharp
namespace Trax.Mediator.Extensions;

public static IServiceCollection AddServiceTrainBus(
    this IServiceCollection serviceCollection,
    ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
    params Assembly[] assemblies
)

public static IServiceCollection RegisterServiceTrains(
    this IServiceCollection services,
    ServiceLifetime serviceLifetime = ServiceLifetime.Transient,
    params Assembly[] assemblies
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `serviceTrainLifetime` / `serviceLifetime` | `ServiceLifetime` | `Transient` | The lifetime each discovered train is registered with: `Transient` or `Scoped` |
| `assemblies` | `Assembly[]` | | The assemblies scanned for non-abstract classes implementing `IServiceTrain<,>` |

**Returns**: the `IServiceCollection`, for chaining.

**Throws**: `ArgumentException` for `ServiceLifetime.Singleton`, because a train instance carries the state of the run in progress. `TrainException` when a train implements two train interfaces, neither extending the other.

## What AddServiceTrainBus registers

| Service | Lifetime |
|---------|----------|
| `ITrainRegistry`, `ITrainDiscoveryService`, `IConcurrencyLimiter`, `ITrustedExecutionScope`, `ICurrentPrincipalProvider` (returns `null`), `IEnqueueContextAccessor`, `IWorkQueuePromotion` | Singleton |
| `ITrainBus`, `IRunExecutor`, `ITrainExecutionService` | Scoped |
| The startup chain check and the authorization registration check | Hosted services, inserted ahead of every hosted service already registered |
| Each discovered train, under its own interface, through `AddScopedTraxRoute` or `AddTransientTraxRoute` | The lifetime given |

`RegisterServiceTrains` registers only the last row. Each train is registered under its own interface (the one deriving from `IServiceTrain<TIn, TOut>`), or under the closed `IServiceTrain<TIn, TOut>` when it has none; see [How Discovery Works](/docs/sdk-reference/configuration/add-mediator#how-discovery-works).

## Package

```
dotnet add package Trax.Mediator
```
