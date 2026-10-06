---
layout: default
title: Route and Junction Registration
description: Reference for the AddScopedTraxRoute family of registration helpers, which register a train or junction under its interface with Inject and CanonicalName.
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 8
---

# Route and Junction Registration

`IServiceCollection` extensions that register a train or junction under its interface, fill its `[Inject]` properties when it is resolved, and set its `CanonicalName` to the interface's full name. A [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) resolved without them has no effect runner and fails on `Run`.

[AddMediator](/docs/sdk-reference/configuration/add-mediator) already registers every train it discovers this way. Call these yourself for a train outside the scanned assemblies, or for a junction you want resolvable from the container.

## Signatures

```csharp
namespace Trax.Effect.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddScopedTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService;
    public static IServiceCollection AddTransientTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService;
    public static IServiceCollection AddSingletonTraxRoute<TService, TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService;

    public static IServiceCollection AddScopedTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddTransientTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);
    public static IServiceCollection AddSingletonTraxRoute(this IServiceCollection services, Type serviceInterface, Type serviceImplementation);

    // Junction aliases: each calls the matching *TraxRoute method
    public static IServiceCollection AddScopedTraxJunction<TService, TImplementation>(this IServiceCollection services) ...;
    public static IServiceCollection AddTransientTraxJunction<TService, TImplementation>(this IServiceCollection services) ...;
    public static IServiceCollection AddSingletonTraxJunction<TService, TImplementation>(this IServiceCollection services) ...;
    // ...and the three Type-based junction overloads
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `TService` / `serviceInterface` | `class` | The interface the route is resolved through. Its `FullName` becomes the `CanonicalName`. |
| `TImplementation` / `serviceImplementation` | `class, TService` | The concrete train or junction |

**Returns**: the `IServiceCollection`, for chaining.

## What a call registers

1. `TImplementation` at the chosen lifetime, as a plain registration.
2. `TService` at the same lifetime, through a factory that resolves `TImplementation`, fills every public writable `[Inject]` property that is still `null` from the container, sets a `CanonicalName` property if the type has one, and returns the instance.

Resolve the interface. Resolving `TImplementation` directly returns an instance with no injected properties and no canonical name.

## Throws

| Method | Exception | When |
|--------|-----------|------|
| `AddSingletonTraxRoute`, `AddSingletonTraxJunction` | `InvalidOperationException` | `TImplementation` derives from `ServiceTrain<,>`. A train instance carries the state of the run in progress, so one shared by the process would mix concurrent runs. Register trains scoped or transient. |

## Example

```csharp
services.AddTransientTraxRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddScopedTraxRoute(typeof(IReconcileTrain), typeof(ReconcileTrain));
services.AddSingletonTraxJunction<IFormatAddress, FormatAddress>();
```

The `*TraxJunction` methods exist for readability; they behave exactly like the `*TraxRoute` methods. See [Train Registration](/docs/cross-cutting/di-registration/train-registration) and [Junction Registration](/docs/cross-cutting/di-registration/junction-registration) for the walkthrough.

## Package

```
dotnet add package Trax.Effect
```
