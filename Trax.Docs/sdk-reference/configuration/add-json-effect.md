---
layout: default
title: AddJson
parent: Configuration
grand_parent: SDK Reference
nav_order: 4
---

# AddJson

Adds JSON change detection for tracking model mutations during train execution. Serializes model state before and after each step to detect changes.

## Signature

```csharp
public static TBuilder AddJson<TBuilder>(
    this TBuilder effectBuilder
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler — callers just write `.AddJson()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

None.

## Returns

`TBuilder` — the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJson()
    )
);
```

## Remarks

- This is a **toggleable** effect — it can be enabled/disabled at runtime via the effect registry.
- Useful for auditing and debugging to see exactly what data each step modified.
- Has a performance cost due to serialization on every step execution. Consider disabling in performance-critical production environments.

## Package

```
dotnet add package Trax.Effect.Provider.Json
```
