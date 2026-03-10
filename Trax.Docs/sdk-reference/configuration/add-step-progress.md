---
layout: default
title: AddStepProgress
parent: Configuration
grand_parent: SDK Reference
nav_order: 7
---

# AddStepProgress

Adds step-level progress tracking and cross-server cancellation checking as step effects. Before each step, checks the database for a cancellation signal and writes the currently running step name to metadata. After each step, clears the progress columns.

## Signature

```csharp
public static TBuilder AddStepProgress<TBuilder>(
    this TBuilder effectBuilder
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler — callers just write `.AddStepProgress()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

None.

## Returns

`TBuilder` — the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddStepProgress()
    )
);
```

## Remarks

- This is a **step-level effect** (runs per step, not per train).
- Registers two providers: `CancellationCheckProvider` (runs first) and `StepProgressProvider`.
- `CancellationCheckProvider` queries the database before each step to check `Metadata.CancellationRequested`. If `true`, it throws `OperationCanceledException`, which `FinishTrain` maps to `TrainState.Cancelled`.
- `StepProgressProvider` sets `Metadata.CurrentlyRunningStep` and `Metadata.StepStartedAt` before each step, and clears them after.
- Both providers are registered as toggleable step effects visible on the dashboard Effects page.
- Requires a data provider (`UsePostgres` or `UseInMemory`) to be registered. **Build-time validation:** If no data provider is configured, the application throws `InvalidOperationException` at startup with a message explaining the required configuration.

## Package

```
dotnet add package Trax.Effect.StepProvider.Progress
```
