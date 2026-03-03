---
layout: default
title: AddInMemoryEffect
parent: Configuration
grand_parent: SDK Reference
nav_order: 2
---

# AddInMemoryEffect

Adds in-memory database support for testing and development. No external database required. Data is lost when the application shuts down.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddInMemoryEffect(
    this Trax.CoreEffectConfigurationBuilder configurationBuilder
)
```

## Parameters

None.

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddInMemoryEffect()
    .AddServiceTrainBus(assemblies: typeof(Program).Assembly)
);
```

## Remarks

- Suitable for unit/integration testing and local development.
- Data does not persist between application restarts.
- Registers an `IDataContext` backed by an in-memory EF Core provider.
- For production use, use [AddPostgresEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}) instead.

## Package

```
dotnet add package Trax.Effect.Data.InMemory
```
