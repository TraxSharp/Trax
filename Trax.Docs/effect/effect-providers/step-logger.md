---
layout: default
title: Step Logger
parent: Effect Providers
grand_parent: Effect
nav_order: 4
---

# Step Logger

The step logger fires before and after each step in a train, logging structured `StepMetadata` entries. This gives you per-step observability: which step is running, how long it took, what its Railway state was, and optionally what it returned.

## Registration

```bash
dotnet add package Trax.Effect.StepProvider.Logging
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddStepLogger(serializeStepData: true)
    )
);
```

*SDK Reference: [AddStepLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-logger.md %})*

## The `serializeStepData` Option

When `serializeStepData` is `false` (the default), the step logger records timing, types, and Railway state but not the actual output values.

When `true`, after each step completes, the logger serializes the step's output to JSON and stores it in `StepMetadata.OutputJson`. This is useful for debugging — you can see exactly what each step produced — but adds serialization overhead per step.

## How It Works

The step logger is a **step effect provider**, not a regular effect provider. It hooks into the `EffectStep` lifecycle rather than the train-level `Track`/`SaveChanges` cycle.

Before each step runs, the logger creates a `StepMetadata` entry with:

| Field | Description |
|-------|-------------|
| `Name` | Step class name |
| `TrainName` | Parent train name |
| `TrainExternalId` | Parent train's external GUID |
| `InputType` / `OutputType` | The step's generic type arguments |
| `StartTimeUtc` | When the step began |

After the step completes:

| Field | Description |
|-------|-------------|
| `EndTimeUtc` | When the step finished |
| `State` | Railway state — `Right` (success), `Left` (failure), or `Bottom` |
| `HasRan` | Whether the step actually executed (skipped on failure track) |
| `OutputJson` | Serialized output (only if `serializeStepData: true`) |

The completed `StepMetadata` is logged at the configured log level via `ILogger<StepLoggerProvider>`.

## Requires EffectStep

The step logger only fires on steps that inherit from `EffectStep<TIn, TOut>`. If your steps use the base `Step<TIn, TOut>`, the logger has nothing to hook into and won't produce any output.

See [Steps: EffectStep vs Step]({{ site.baseurl }}{% link core/trains-and-steps.md %}#effectstep-vs-step) for the difference between the two.

## When to Use It

- **Debugging slow trains** — The timing data shows which step is the bottleneck.
- **Tracing failures** — The Railway state tells you exactly where and why a train entered the failure track.
- **Development** — Pair with `serializeStepData: true` to see step-by-step data flow through the chain.
