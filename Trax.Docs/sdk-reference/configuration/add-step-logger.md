---
layout: default
title: AddStepLogger
parent: Configuration
grand_parent: SDK Reference
nav_order: 6
---

# AddStepLogger

Adds per-step execution logging as a step-level effect. Records individual step metadata (name, duration, input/output types) for each step in the train.

## Signature

```csharp
public static TBuilder AddStepLogger<TBuilder>(
    this TBuilder effectBuilder,
    bool serializeStepData = false
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler — callers just write `.AddStepLogger()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `serializeStepData` | `bool` | No | `false` | Whether to serialize step input/output data. Adds detail but increases storage and may impact performance. |

## Returns

`TBuilder` — the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddStepLogger(serializeStepData: true)
    )
);
```

## Remarks

- This is a **step-level effect** (runs per step, not per train).
- Step metadata includes: step name, start/end times, duration, input/output types.
- When `serializeStepData` is `true`, the actual step input and output values are serialized to JSON.
- Registered as a toggleable effect.

## Package

```
dotnet add package Trax.Effect.StepProvider.Logging
```
