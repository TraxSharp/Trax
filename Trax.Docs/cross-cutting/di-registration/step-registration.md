---
layout: default
title: Step Registration
parent: DI Registration
grand_parent: Cross-Cutting
nav_order: 2
---

# Step Registration

Extension methods for registering Trax.Core steps with `[Inject]` property injection support. These are **aliases** for the corresponding [Train Registration]({{ site.baseurl }}{% link cross-cutting/di-registration/train-registration.md %}) methods — the injection behavior is identical.

## Signatures

### Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddTransientTraxStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddSingletonTraxStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService
```

### Non-Generic Overloads

```csharp
public static IServiceCollection AddScopedTraxStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddTransientTraxStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddSingletonTraxStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)
```

## Example

```csharp
services.AddScopedTraxStep<IValidateOrderStep, ValidateOrderStep>();
services.AddTransientTraxStep<IProcessPaymentStep, ProcessPaymentStep>();
```

## Remarks

- These methods delegate directly to the train registration equivalents. They exist for semantic clarity — `AddTraxStep` communicates intent better than `AddTraxRoute` when registering steps.
- Steps typically don't need manual DI registration unless they use `[Inject]` properties. Most steps are created by the train's `Chain<TStep>()` method using Memory-based constructor injection.
