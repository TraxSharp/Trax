---
layout: default
title: AddJunctionProgress
parent: Configuration
grand_parent: SDK Reference
nav_order: 7
---

# AddJunctionProgress

Adds junction-level progress tracking and cross-server cancellation checking as junction effects. Before each junction, checks the database for a cancellation signal and writes the currently running junction name to metadata. After each junction, clears the progress columns.

## Signature

```csharp
public static TBuilder AddJunctionProgress<TBuilder>(
    this TBuilder effectBuilder
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.AddJunctionProgress()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

None.

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionProgress()
    )
);
```

## Remarks

- This is a **junction-level effect** (runs per junction, not per train).
- Registers two providers: `CancellationCheckProvider` (runs first) and `JunctionProgressProvider`.
- `CancellationCheckProvider` queries the database before each junction to check `Metadata.CancellationRequested`. If `true`, it throws `OperationCanceledException`, which `FinishTrain` maps to `TrainState.Cancelled`.
- `JunctionProgressProvider` sets `Metadata.CurrentlyRunningJunction` and `Metadata.JunctionStartedAt` before each junction, and clears them after.
- Both providers are registered as toggleable junction effects visible on the dashboard Effects page.
- Requires a data provider (`UsePostgres` or `UseInMemory`) to be registered. **Build-time validation:** If no data provider is configured, the application throws `InvalidOperationException` at startup with a message explaining the required configuration.

## Package

```
dotnet add package Trax.Effect.JunctionProvider.Progress
```
