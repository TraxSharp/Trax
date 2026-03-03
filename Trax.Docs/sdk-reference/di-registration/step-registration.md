---
layout: default
title: Step Registration
parent: DI Registration
grand_parent: SDK Reference
nav_order: 2
---

# Step Registration

Extension methods for registering Trax.Core steps with `[Inject]` property injection support. These are **aliases** for the corresponding [Train Registration]({{ site.baseurl }}{% link sdk-reference/di-registration/train-registration.md %}) methods — the injection behavior is identical.

## Signatures

### Generic Overloads

```csharp
public static IServiceCollection AddScopedTrax.CoreStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddTransientTrax.CoreStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService

public static IServiceCollection AddSingletonTrax.CoreStep<TService, TImplementation>(
    this IServiceCollection services
) where TService : class where TImplementation : class, TService
```

### Non-Generic Overloads

```csharp
public static IServiceCollection AddScopedTrax.CoreStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddTransientTrax.CoreStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)

public static IServiceCollection AddSingletonTrax.CoreStep(
    this IServiceCollection services, Type serviceInterface, Type serviceImplementation
)
```

## Example

```csharp
services.AddScopedTrax.CoreStep<IValidateOrderStep, ValidateOrderStep>();
services.AddTransientTrax.CoreStep<IProcessPaymentStep, ProcessPaymentStep>();
```

## Remarks

- These methods delegate directly to the train registration equivalents. They exist for semantic clarity — `AddTrax.CoreStep` communicates intent better than `AddTrax.CoreRoute` when registering steps.
- Steps typically don't need manual DI registration unless they use `[Inject]` properties. Most steps are created by the train's `Chain<TStep>()` method using Memory-based constructor injection.
