---
layout: default
title: AddJson
description: Reference for AddJson, the effect that logs a tracked model's JSON whenever it changes between saves, without storing anything in the database.
parent: Configuration
grand_parent: SDK Reference
nav_order: 4
---

# AddJson

Adds JSON change detection for the models a train tracks (its `Metadata`, and anything else passed to `Track`). Each model is serialized when it is first tracked; each time the train saves its effects (when the run starts and when it finishes, and with [`AddJunctionProgress`](/docs/sdk-reference/configuration/add-junction-progress) also before and after each junction) every tracked model is serialized again, and one whose JSON changed is written to `ILogger` at the [effect log level](/docs/sdk-reference/configuration/set-effect-log-level). Nothing is stored in the database.

## Signature

```csharp
public static TBuilder AddJson<TBuilder>(
    this TBuilder effectBuilder
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.AddJson()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

None.

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

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

- This is a **toggleable** effect that can be enabled/disabled at runtime via the effect registry.
- Useful for debugging: it shows what a tracked model looked like each time it changed between saves. It cannot attribute a change to a junction.
- Has a cost: every tracked model is serialized on every save, changed or not. Consider disabling it in performance-critical production environments.

## Package

```
dotnet add package Trax.Effect.Provider.Json
```
