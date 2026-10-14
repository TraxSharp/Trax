---
title: Route and Junction Registration
description: "Reference for AddScopedTraxRoute, AddTransientTraxRoute, AddSingletonTraxRoute and their TraxJunction aliases, which register a train or junction."
type: reference
area: trains
package: Trax.Effect
namespace: Trax.Effect.Extensions
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 8
redirect_from:
  - /docs/cross-cutting/di-registration
  - /docs/cross-cutting/di-registration/junction-registration
  - /docs/cross-cutting/di-registration/train-registration
---

# Route and Junction Registration

`IServiceCollection` extensions that register a train or junction under its interface, fill its `[Inject]` properties when it is resolved, and set its `CanonicalName` to the interface's full name. They wrap `AddScoped`, `AddTransient` and `AddSingleton`. The `*TraxJunction` methods are aliases of the `*TraxRoute` methods with the same lifetime.

## Signatures

```csharp fragment
namespace Trax.Effect.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddScopedTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;
    public static IServiceCollection AddTransientTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;
    public static IServiceCollection AddSingletonTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;

    public static IServiceCollection AddScopedTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddTransientTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddSingletonTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);

    public static IServiceCollection AddScopedTraxJunction<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;
    public static IServiceCollection AddTransientTraxJunction<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;
    public static IServiceCollection AddSingletonTraxJunction<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService;

    public static IServiceCollection AddScopedTraxJunction(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddTransientTraxJunction(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddSingletonTraxJunction(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void InjectProperties(this IServiceProvider serviceProvider, object instance);
}
```

The same class holds `AddTrax`, `AddEffects`, `AddEffect`, `AddJunctionEffect` and `AddLifecycleHook`, documented under [Configuration](/docs/sdk-reference/configuration).

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `TService` / `serviceInterface` | `class` | The interface the route is resolved through, such as `ICreateOrderTrain`. Its `FullName` becomes the `CanonicalName`. |
| `TImplementation` / `serviceImplementation` | `class, TService` | The concrete train or junction |

## Returns

The `IServiceCollection`, for chaining.

## Behavior

### Registered services

| Service | Lifetime | Resolves to |
|---------|----------|-------------|
| `TImplementation` | The method's | A plain registration of the concrete class, with no property injection |
| `TService` | The method's | A factory that resolves `TImplementation`, calls `InjectProperties` on it, sets its public `CanonicalName` property (when it has one) to `TService.FullName`, and returns it |

Resolve the interface. Resolving `TImplementation` directly returns an instance with no injected properties and no canonical name, and a `ServiceTrain` resolved that way throws on `Run` because its framework services are `null`.

`InjectProperties(instance)` fills every public instance property that carries [`[Inject]`](/docs/sdk-reference/attributes/inject), has a setter and is still `null`: an `IEnumerable<T>` with every registered `T`, anything else with `GetService(type)`, left `null` when nothing is registered. It is public for these factories and hidden from completion.

`CanonicalName` is the train's identity in metadata, work queue entries and subscription matching. See [Canonical Train Naming](/docs/effect/architecture#canonical-train-naming).

### Trains

[AddMediator](/docs/sdk-reference/configuration/add-mediator) registers every train it discovers this way: through `AddTransientTraxRoute` by default, or `AddScopedTraxRoute` when `TrainLifetime(ServiceLifetime.Scoped)` is set. `TrainLifetime(ServiceLifetime.Singleton)` throws `ArgumentException`. A train outside the scanned assemblies is registered with these methods directly.

A resolved train instance carries the state of one run: its metadata row, effect runner and data context. Each `Run` call records its own metadata row, so a scoped instance run twice in one scope records two runs.

### Junctions

A registered junction is used only when the train reaches it through [IChain](/docs/sdk-reference/train-methods/i-chain), which resolves it from Memory or the container by its interface, `[Inject]` properties included. [Chain](/docs/sdk-reference/train-methods/chain) never reads the registration: it builds the junction through its constructor, and its `[Inject]` properties stay `null` even when the junction is registered. The startup chain check logs a warning for each such property.

### Throws

| Method | Exception | When |
|--------|-----------|------|
| `AddSingletonTraxRoute`, `AddSingletonTraxJunction` | `InvalidOperationException` | `TImplementation` derives from `ServiceTrain<,>`: `<Train> is a service train and cannot be registered as a singleton (<IService>)`. An instance carries the state of the run in progress. |

## Example

```csharp compile
using Trax.Effect.Extensions;

services.AddTransientTraxRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddScopedTraxRoute(typeof(IReconcileTrain), typeof(ReconcileTrain));
services.AddScopedTraxJunction<IFormatAddress, FormatAddress>();
```

## See also

- [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), what the injected properties are
- [Inject](/docs/sdk-reference/attributes/inject)
- [AddMediator](/docs/sdk-reference/configuration/add-mediator), which registers discovered trains
