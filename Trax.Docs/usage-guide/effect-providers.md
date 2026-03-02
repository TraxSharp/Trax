---
layout: default
title: Effect Providers
parent: Usage Guide
nav_order: 9
has_children: true
---

# Configuring Effect Providers

Effect providers handle the side effects that happen when a train runs—database writes, logging, serialization. Each provider is independent: add or remove any of them without changing your train code. For the conceptual background, see [Effects](../concepts/functional-programming.md#effects).

## Database Persistence (Postgres or InMemory)

**Use when:** You need to query train history, audit execution, or debug production issues.

```csharp
// Production
services.AddTrax.CoreEffects(options =>
    options.AddPostgresEffect("Host=localhost;Database=app;Username=postgres;Password=pass")
);

// Testing
services.AddTrax.CoreEffects(options =>
    options.AddInMemoryEffect()
);
```

*API Reference: [AddPostgresEffect]({{ site.baseurl }}{% link api-reference/configuration/add-postgres-effect.md %}), [AddInMemoryEffect]({{ site.baseurl }}{% link api-reference/configuration/add-in-memory-effect.md %})*

This persists a `Metadata` record for each train execution containing:
- Train name and state (Pending → InProgress → Completed/Failed)
- Start and end timestamps
- Serialized input and output
- Exception details if failed
- Parent train ID for nested trains

See [Data Persistence](effect-providers/data-persistence.md) for the full breakdown of both backends, what gets persisted, and DataContext logging.

## JSON Effect (`AddJsonEffect`)

**Use when:** Debugging during development. Logs train state changes to your configured logger.

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddJsonEffect()
);
```

*API Reference: [AddJsonEffect]({{ site.baseurl }}{% link api-reference/configuration/add-json-effect.md %})*

This doesn't persist anything—it just logs. Useful for seeing what's happening without setting up a database.

See [JSON Effect](effect-providers/json-effect.md) for how change detection works.

## Parameter Effect (`SaveTrainParameters`)

**Use when:** You need to store train inputs/outputs in the database for later querying or replay.

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)
        .SaveTrainParameters()  // Serializes Input/Output to Metadata
);
```

*API Reference: [SaveTrainParameters]({{ site.baseurl }}{% link api-reference/configuration/save-train-parameters.md %})*

Without this, the `Input` and `Output` columns in `Metadata` are null. With it, they contain JSON-serialized versions of your request and response objects. You can control which parameters are saved:

```csharp
.SaveTrainParameters(configure: cfg =>
{
    cfg.SaveInputs = true;
    cfg.SaveOutputs = false;  // Skip output serialization
})
```

This configuration can also be changed at runtime from the dashboard's [Effects page](../dashboard.md#effects-page).

See [Parameter Effect](effect-providers/parameter-effect.md) for details, custom serialization options, and configuration properties.

## Step Logger (`AddStepLogger`)

**Use when:** You want structured logging for individual step executions inside a train.

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddStepLogger(serializeStepData: true)
);
```

*API Reference: [AddStepLogger]({{ site.baseurl }}{% link api-reference/configuration/add-step-logger.md %})*

This hooks into `EffectStep` (not base `Step`) lifecycle events. Before and after each step runs, it logs structured `StepMetadata` containing the step name, input/output types, timing, and Railway state (`Right`/`Left`). When `serializeStepData` is `true`, the step's output is also serialized to JSON in the log entry.

Requires steps to inherit from `EffectStep<TIn, TOut>` instead of `Step<TIn, TOut>`. See [EffectStep vs Step](steps.md#effectstep-vs-step).

See [Step Logger](effect-providers/step-logger.md) for the full StepMetadata field reference.

## Step Progress & Cancellation Check (`AddStepProgress`)

**Use when:** You need per-step progress visibility in the dashboard and/or the ability to cancel running trains from the dashboard (including cross-server cancellation).

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddStepProgress()
);
```

*API Reference: [AddStepProgress]({{ site.baseurl }}{% link api-reference/configuration/add-step-progress.md %})*

This registers two step-level effect providers:

1. **CancellationCheckProvider** — Before each step, queries the database for `Metadata.CancellationRequested`. If `true`, throws `OperationCanceledException`, which maps to `TrainState.Cancelled`.
2. **StepProgressProvider** — Before each step, writes the step name and start time to `Metadata.CurrentlyRunningStep` and `Metadata.StepStartedAt`. After the step, clears both columns.

The cancellation check runs first so a cancelled train never writes progress columns for a step that won't execute. Requires steps to inherit from `EffectStep<TIn, TOut>`.

See [Step Progress](effect-providers/step-progress.md) for the dual-path cancellation architecture and dashboard integration.

## Combining Providers

Providers compose. A typical production setup:

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)   // Persist metadata
        .SaveTrainParameters()              // Include input/output in metadata
        .AddStepLogger(serializeStepData: true) // Log individual step executions
        .AddStepProgress()                     // Step progress + cancellation check
        .AddServiceTrainBus(assemblies)      // Enable train discovery
);
```

A typical development setup:

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddInMemoryEffect()                   // Fast, no database needed
        .AddJsonEffect()                       // Log state changes
        .AddStepLogger()                       // Log step executions
        .AddServiceTrainBus(assemblies)
);
```
