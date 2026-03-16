---
layout: default
title: AddJunctionLogger
parent: Configuration
grand_parent: SDK Reference
nav_order: 6
---

# AddJunctionLogger

Adds per-junction execution logging as a junction-level effect. Records individual junction metadata (name, duration, input/output types) for each junction in the train.

## Signature

```csharp
public static TBuilder AddJunctionLogger<TBuilder>(
    this TBuilder effectBuilder,
    bool serializeJunctionData = false
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler — callers just write `.AddJunctionLogger()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `serializeJunctionData` | `bool` | No | `false` | Whether to serialize junction input/output data. Adds detail but increases storage and may impact performance. |

## Returns

`TBuilder` — the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionLogger(serializeJunctionData: true)
    )
);
```

## Remarks

- This is a **junction-level effect** (runs per junction, not per train).
- Junction metadata includes: junction name, start/end times, duration, input/output types.
- When `serializeJunctionData` is `true`, the actual junction input and output values are serialized to JSON.
- Registered as a toggleable effect.

## Package

```
dotnet add package Trax.Effect.JunctionProvider.Logging
```
