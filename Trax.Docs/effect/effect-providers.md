---
layout: default
title: Effect Providers
parent: Effect
nav_order: 2
has_children: true
---

# Configuring Effect Providers

Effect providers are the station services that handle side effects as a train runs — database writes, logging, serialization. Each provider is independent: add or remove any of them without changing your train code. For the conceptual background, see [Effect overview]({{ site.baseurl }}{% link effect.md %}).

## Database Persistence (Postgres or InMemory)

**Use when:** You need to query train history, audit execution, or debug production issues.

```csharp
// Production
services.AddTraxEffects(options =>
    options.AddPostgresEffect("Host=localhost;Database=app;Username=postgres;Password=pass")
);

// Testing
services.AddTraxEffects(options =>
    options.AddInMemoryEffect()
);
```

*SDK Reference: [AddPostgresEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}), [AddInMemoryEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %})*

This persists a `Metadata` record for each train execution containing:
- Train name and state (Pending -> InProgress -> Completed/Failed)
- Start and end timestamps
- Serialized input and output
- Exception details if failed
- Parent train ID for nested trains

See [Data Persistence](effect-providers/data-persistence.md) for the full breakdown of both backends, what gets persisted, and DataContext logging.

## JSON Effect (`AddJsonEffect`)

**Use when:** Debugging during development. Logs train state changes to your configured logger.

```csharp
services.AddTraxEffects(options =>
    options.AddJsonEffect()
);
```

*SDK Reference: [AddJsonEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-json-effect.md %})*

This doesn't persist anything — it just logs. Useful for seeing what's happening without setting up a database.

See [JSON Effect](effect-providers/json-effect.md) for how change detection works.

## Parameter Effect (`SaveTrainParameters`)

**Use when:** You need to store train inputs/outputs in the database for later querying or replay.

```csharp
services.AddTraxEffects(options =>
    options
        .AddPostgresEffect(connectionString)
        .SaveTrainParameters()  // Serializes Input/Output to Metadata
);
```

*SDK Reference: [SaveTrainParameters]({{ site.baseurl }}{% link sdk-reference/configuration/save-train-parameters.md %})*

Without this, the `Input` and `Output` columns in `Metadata` are null. With it, they contain JSON-serialized versions of your request and response objects. You can control which parameters are saved:

```csharp
.SaveTrainParameters(configure: cfg =>
{
    cfg.SaveInputs = true;
    cfg.SaveOutputs = false;  // Skip output serialization
})
```

This configuration can also be changed at runtime from the dashboard's [Effects page]({{ site.baseurl }}{% link dashboard.md %}#effects-page).

See [Parameter Effect](effect-providers/parameter-effect.md) for details, custom serialization options, and configuration properties.

## Step Logger (`AddStepLogger`)

**Use when:** You want structured logging for individual step executions inside a train.

```csharp
services.AddTraxEffects(options =>
    options.AddStepLogger(serializeStepData: true)
);
```

*SDK Reference: [AddStepLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-logger.md %})*

This hooks into `EffectStep` (not base `Step`) lifecycle events. Before and after each step runs, it logs structured `StepMetadata` containing the step name, input/output types, timing, and Railway state (`Right`/`Left`). When `serializeStepData` is `true`, the step's output is also serialized to JSON in the log entry.

Requires steps to inherit from `EffectStep<TIn, TOut>` instead of `Step<TIn, TOut>`. See [EffectStep vs Step]({{ site.baseurl }}{% link core/trains-and-steps.md %}#effectstep-vs-step).

See [Step Logger](effect-providers/step-logger.md) for the full StepMetadata field reference.

## Step Progress & Cancellation Check (`AddStepProgress`)

**Use when:** You need per-step progress visibility in the dashboard and/or the ability to cancel running trains from the dashboard (including cross-server cancellation).

```csharp
services.AddTraxEffects(options =>
    options.AddStepProgress()
);
```

*SDK Reference: [AddStepProgress]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-progress.md %})*

This registers two step-level effect providers:

1. **CancellationCheckProvider** — Before each step, queries the database for `Metadata.CancellationRequested`. If `true`, throws `OperationCanceledException`, which maps to `TrainState.Cancelled`.
2. **StepProgressProvider** — Before each step, writes the step name and start time to `Metadata.CurrentlyRunningStep` and `Metadata.StepStartedAt`. After the step, clears both columns.

The cancellation check runs first so a cancelled train never writes progress columns for a step that won't execute. Requires steps to inherit from `EffectStep<TIn, TOut>`.

See [Step Progress](effect-providers/step-progress.md) for the dual-path cancellation architecture and dashboard integration.

## Lifecycle Hooks (`AddLifecycleHook`)

**Use when:** You want side effects to fire on train state transitions — notifications, metrics, real-time updates — without coupling your train code to those concerns.

```csharp
services.AddTraxEffects(options =>
    options.AddLifecycleHook<SlackNotificationHookFactory>()
);
```

*SDK Reference: [AddLifecycleHook]({{ site.baseurl }}{% link sdk-reference/configuration/add-lifecycle-hook.md %})*

Lifecycle hooks implement `ITrainLifecycleHook` and fire at four points: `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled`. Unlike effect providers, hook exceptions are **caught and logged, never propagated** — a failing Slack webhook will never derail a train.

The `Trax.Api.GraphQL` package includes a built-in hook (`GraphQLSubscriptionHook`) that publishes lifecycle events to [GraphQL subscriptions]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}) over WebSocket. It's automatically registered by `AddTraxGraphQL()`. Only trains decorated with [`[TraxBroadcast]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-subscription-attribute.md %}) have their events published.

## Combining Providers

Providers compose. A typical production setup:

```csharp
services.AddTraxEffects(options =>
    options
        .AddPostgresEffect(connectionString)   // Persist metadata
        .SaveTrainParameters()                 // Include input/output in metadata
        .AddStepLogger(serializeStepData: true) // Log individual step executions
        .AddStepProgress()                     // Step progress + cancellation check
        .AddServiceTrainBus(assemblies)        // Enable train discovery
);
```

A typical development setup:

```csharp
services.AddTraxEffects(options =>
    options
        .AddInMemoryEffect()                   // Fast, no database needed
        .AddJsonEffect()                       // Log state changes
        .AddStepLogger()                       // Log step executions
        .AddServiceTrainBus(assemblies)
);
```
