---
layout: default
title: AddJsonEffect
parent: Configuration
grand_parent: SDK Reference
nav_order: 4
---

# AddJsonEffect

Adds JSON change detection for tracking model mutations during train execution. Serializes model state before and after each step to detect changes.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddJsonEffect(
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
    .AddPostgresEffect(connectionString)
    .AddJsonEffect()
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
