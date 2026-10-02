---
layout: default
title: Junction Registration
description: The AddTraxJunction registration methods, aliases of the train registration methods with the same Inject property injection, and when a junction needs one.
parent: DI Registration
grand_parent: Cross-Cutting
nav_order: 2
---

# Junction Registration

Extension methods for registering Trax.Core junctions with `[Inject]` property injection support. These are **aliases** for the corresponding [Train Registration](/docs/cross-cutting/di-registration/train-registration) methods, and the injection behavior is identical.

## Signatures

### Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxJunction<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddTransientTraxJunction<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddSingletonTraxJunction<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService
```

### Non-Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxJunction(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddTransientTraxJunction(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddSingletonTraxJunction(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)
```

## Example

```csharp
services.AddScopedTraxJunction<IValidateOrderJunction, ValidateOrderJunction>();
services.AddTransientTraxJunction<IProcessPaymentJunction, ProcessPaymentJunction>();
```

## Remarks

- These methods delegate directly to the train registration equivalents. They exist for semantic clarity. `AddTraxJunction` communicates intent better than `AddTraxRoute` when registering junctions.
- Registration only matters for a junction the train reaches through `IChain<TInterface>()`, which resolves the registered service from the container, `[Inject]` properties included. `Chain<TJunction>()` never consults the registration: it calls the junction's public constructor with arguments taken from Memory and the container, and does not populate `[Inject]` properties, so on a junction built that way they stay `null` even when the junction is registered. Give a `Chain<T>()` junction its dependencies as constructor parameters, or register it under an interface and reach it with `IChain`.

## SDK Reference

> [Chain / IChain](/docs/sdk-reference/train-methods/chain) | [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration)
