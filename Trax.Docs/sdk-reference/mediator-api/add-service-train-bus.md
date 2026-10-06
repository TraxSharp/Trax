---
layout: default
title: AddServiceTrainBus
description: Reference for AddServiceTrainBus and RegisterServiceTrains, the registration AddMediator performs, and what it registers when called on its own.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 11
---

# AddServiceTrainBus

The `IServiceCollection` registration that [AddMediator](/docs/sdk-reference/configuration/add-mediator) performs: the train bus, the registry, discovery, execution, the concurrency limiter, the trusted scope, the startup checks, and every train found in the given assemblies. `RegisterServiceTrains` is the part of it that registers the trains alone.

Prefer `AddMediator`, which also takes the mediator's settings (concurrency limits, `AllowMissingAuthorizationService()`, `SkipChainVerification()` and the rest). Called on its own, `AddServiceTrainBus` registers a `MediatorConfiguration` with the defaults (the given lifetime and assemblies, authorization required, chains verified, no concurrency limits) when none is registered yet, so the host it builds starts. A `MediatorConfiguration` already registered, as `AddMediator` registers its own first, is kept. Effects still come from `AddTrax`. Before Trax.Mediator 1.25.0 it registered no `MediatorConfiguration`, and a host built with it alone failed to resolve the services and startup checks that need one.

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
| `MediatorConfiguration`, with the defaults, when none is registered | Singleton |
| `ITrainRegistry`, `ITrainDiscoveryService`, `IConcurrencyLimiter`, `ITrustedExecutionScope`, `ICurrentPrincipalProvider` (returns `null`), `IEnqueueContextAccessor`, `IWorkQueuePromotion` | Singleton |
| `ITrainBus`, `IRunExecutor`, `ITrainExecutionService` | Scoped |
| The startup chain check and the authorization registration check | Hosted services, inserted ahead of every hosted service already registered |
| Each discovered train, under its own interface, through `AddScopedTraxRoute` or `AddTransientTraxRoute` | The lifetime given |

`RegisterServiceTrains` registers only the last row. Each train is registered under its own interface (the one deriving from `IServiceTrain<TIn, TOut>`), or under the closed `IServiceTrain<TIn, TOut>` when it has none; see [How Discovery Works](/docs/sdk-reference/configuration/add-mediator#how-discovery-works).

## Package

```
dotnet add package Trax.Mediator
```
