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

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.AddJunctionLogger()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `serializeJunctionData` | `bool` | No | `false` | Whether to serialize each junction's output. Adds detail but increases log volume and serialization work per junction. |

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

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
- When `serializeJunctionData` is `true`, each successful junction's output is serialized to JSON (junction input is not). Members marked `[TraxSensitive]` are written as `{"_redacted": true}`, and the copies a train is run from (a manifest's `Properties`, a queued entry's `Input`, a background job's `Input`) as `{"_omitted": true}`.
- The output is not serialized for a train whose output `SaveTrainParameters` excludes, is bounded by `MaxParameterBytes` (1 MiB by default) past which it is written as `{"_truncated": true, "_maxBytes": N}`, and is written as `{"_unserializable": true, "_error": "<exception type>"}` when it cannot be serialized. Serializing it never fails the train. See [Junction Logger](/docs/effect/effect-providers/junction-logger#the-serializejunctiondata-option).
- Registered as a toggleable effect.

## Package

```
dotnet add package Trax.Effect.JunctionProvider.Logging
```
