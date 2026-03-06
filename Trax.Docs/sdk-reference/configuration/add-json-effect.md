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
public static TraxEffectBuilder AddJson(
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
