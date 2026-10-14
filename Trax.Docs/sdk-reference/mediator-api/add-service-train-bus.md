---
title: AddServiceTrainBus
description: Reference for AddServiceTrainBus and RegisterServiceTrains, the IServiceCollection registration AddMediator performs, and what it registers on its own.
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 13
package: Trax.Mediator
namespace: Trax.Mediator.Extensions
source:
  - Trax.Mediator/src/Trax.Mediator/Extensions/ServiceExtensions.cs
---

# AddServiceTrainBus

The `IServiceCollection` registration that [AddMediator](/docs/sdk-reference/configuration/add-mediator)
performs: the train bus, the registry, discovery, execution, the concurrency limiter, the trusted
scope, the startup checks, and every train found in the given assemblies. `RegisterServiceTrains`
is the part of it that registers the trains alone.

## Signatures

```csharp fragment
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

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `serviceTrainLifetime` / `serviceLifetime` | `ServiceLifetime` | `Transient` | The lifetime each discovered train is registered with: `Transient` or `Scoped` |
| `assemblies` | `Assembly[]` | none | The assemblies scanned for non-abstract classes implementing `IServiceTrain<,>` |

## Returns

The `IServiceCollection`, for chaining.

## Behavior

`AddMediator` calls `AddServiceTrainBus` after registering its own `MediatorConfiguration`, and
takes the mediator's settings (concurrency limits, `AllowMissingAuthorizationService()`,
`SkipChainVerification()` and the rest), which `AddServiceTrainBus` has no parameter for. Called on
its own, `AddServiceTrainBus` registers a `MediatorConfiguration` with the defaults (the given
lifetime and assemblies, authorization required, chains verified, no concurrency limits) when none
is registered yet. A `MediatorConfiguration` already registered is kept. Effects still come from
`AddTrax`. Before Trax.Mediator 1.25.0 it registered no `MediatorConfiguration`, and a host built
with it alone failed to resolve the services and startup checks that need one.

Both throw `ArgumentException` for `ServiceLifetime.Singleton`, because a train instance carries
the state of the run in progress, and `TrainException` when a train implements two train
interfaces, neither extending the other.

### Registered services

| Service | Lifetime |
|---------|----------|
| `MediatorConfiguration`, with the defaults, when none is registered | Singleton |
| `ITrainRegistry` (its `InputTypeToTrain` map is the first train scanned for each input type, which `ITrainBus.RunAsync` dispatches to), `ITrainDiscoveryService`, `ITrainChainGraphs`, `IConcurrencyLimiter`, `ITrustedExecutionScope`, `ICurrentPrincipalProvider` (returns `null`), `IEnqueueContextAccessor`, `IWorkQueuePromotion`, and the `IServiceCollection` itself, which discovery reads | Singleton |
| `ITrainBus`, `IRunExecutor` (`LocalRunExecutor`), `ITrainExecutionService`, `IInvokedTrainLauncher` (queues the train a state machine's invoking state runs) | Scoped |
| The startup chain check and the authorization registration check | Hosted services, inserted ahead of every hosted service already registered |
| Each discovered train, under its own interface, through `AddScopedTraxRoute` or `AddTransientTraxRoute` | The lifetime given |

`RegisterServiceTrains` registers only the last row. Each train is registered under its own
interface (the one deriving from `IServiceTrain<TIn, TOut>`), or under the closed
`IServiceTrain<TIn, TOut>` when it has none; see
[How Discovery Works](/docs/sdk-reference/configuration/add-mediator#how-discovery-works).

## Example

```csharp compile
services.AddServiceTrainBus(ServiceLifetime.Scoped, typeof(OrderInput).Assembly);
```

## See also

- [AddMediator](/docs/sdk-reference/configuration/add-mediator), the registration to call from a Trax host
- [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration)
- [Mediator](/docs/mediator)
