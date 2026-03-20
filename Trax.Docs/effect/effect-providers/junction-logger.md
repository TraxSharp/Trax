---
layout: default
title: Junction Logger
parent: Effect Providers
grand_parent: Effect
nav_order: 4
---

# Junction Logger

The junction logger fires before and after each junction in a train, logging structured `JunctionMetadata` entries. This gives you per-junction observability: which junction is running, how long it took, what its Railway state was, and optionally what it returned.

> [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)

## Registration

```bash
dotnet add package Trax.Effect.JunctionProvider.Logging
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionLogger(serializeJunctionData: true)
    )
);
```

## The `serializeJunctionData` Option

When `serializeJunctionData` is `false` (the default), the junction logger records timing, types, and Railway state but not the actual output values.

When `true`, after each junction completes, the logger serializes the junction's output to JSON and stores it in `JunctionMetadata.OutputJson`. This is useful for debugging — you can see exactly what each junction produced — but adds serialization overhead per junction.

## How It Works

The junction logger is a **junction effect provider**, not a regular effect provider. It hooks into the `EffectJunction` lifecycle rather than the train-level `Track`/`SaveChanges` cycle.

Before each junction runs, the logger creates a `JunctionMetadata` entry with:

| Field | Description |
|-------|-------------|
| `Name` | Junction class name |
| `TrainName` | Parent train name |
| `TrainExternalId` | Parent train's external GUID |
| `InputType` / `OutputType` | The junction's generic type arguments |
| `StartTimeUtc` | When the junction began |

After the junction completes:

| Field | Description |
|-------|-------------|
| `EndTimeUtc` | When the junction finished |
| `State` | Railway state — `Right` (success), `Left` (failure), or `Bottom` |
| `HasRan` | Whether the junction actually executed (skipped on failure track) |
| `OutputJson` | Serialized output (only if `serializeJunctionData: true`) |

The completed `JunctionMetadata` is logged at the configured log level via `ILogger<JunctionLoggerProvider>`.

## Requires EffectJunction

The junction logger only fires on junctions that inherit from `EffectJunction<TIn, TOut>`. If your junctions use the base `Junction<TIn, TOut>`, the logger has nothing to hook into and won't produce any output.

See [Junctions: EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction) for the difference between the two.

## When to Use It

- **Debugging slow trains** — The timing data shows which junction is the bottleneck.
- **Tracing failures** — The Railway state tells you exactly where and why a train switched to the left track.
- **Development** — Pair with `serializeJunctionData: true` to see junction-by-junction data flow through the chain.
