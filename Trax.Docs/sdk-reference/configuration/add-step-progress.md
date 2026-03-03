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
public static Trax.CoreEffectConfigurationBuilder AddStepProgress(
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
    .AddStepProgress()
);
```

## Remarks

- This is a **step-level effect** (runs per step, not per train).
- Registers two providers: `CancellationCheckProvider` (runs first) and `StepProgressProvider`.
- `CancellationCheckProvider` queries the database before each step to check `Metadata.CancellationRequested`. If `true`, it throws `OperationCanceledException`, which `FinishTrain` maps to `TrainState.Cancelled`.
- `StepProgressProvider` sets `Metadata.CurrentlyRunningStep` and `Metadata.StepStartedAt` before each step, and clears them after.
- Both providers are registered as toggleable step effects visible on the dashboard Effects page.
- Requires a database effect (`AddPostgresEffect` or `AddInMemoryEffect`) to be registered.

## Package

```
dotnet add package Trax.Effect.StepProvider.Progress
```
