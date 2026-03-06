---
layout: default
title: UseInMemory
parent: Configuration
grand_parent: SDK Reference
nav_order: 2
---

# UseInMemory

Adds in-memory database support for testing and development. No external database required. Data is lost when the application shuts down.

## Signature

```csharp
public static TraxEffectBuilder UseInMemory(
    this TraxEffectBuilder effectBuilder
)
```

## Parameters

None.

## Returns

`TraxEffectBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
    .AddMediator(typeof(Program).Assembly)
);
```

## Remarks

- Suitable for unit/integration testing and local development.
- Data does not persist between application restarts.
- Registers an `IDataContext` backed by an in-memory EF Core provider.
- For production use, use [UsePostgres]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}) instead.

## Package

```
dotnet add package Trax.Effect.Data.InMemory
```
