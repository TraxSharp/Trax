---
layout: default
title: Junction Registration
parent: DI Registration
grand_parent: Cross-Cutting
nav_order: 2
---

# Junction Registration

Extension methods for registering Trax.Core junctions with `[Inject]` property injection support. These are **aliases** for the corresponding [Train Registration]({{ site.baseurl }}{% link cross-cutting/di-registration/train-registration.md %}) methods — the injection behavior is identical.

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

- These methods delegate directly to the train registration equivalents. They exist for semantic clarity — `AddTraxJunction` communicates intent better than `AddTraxRoute` when registering junctions.
- Junctions typically don't need manual DI registration unless they use `[Inject]` properties. Most junctions are created by the train's `Chain<TJunction>()` method using Memory-based constructor injection.
