---
layout: default
title: Effect Providers
parent: Effect
nav_order: 2
has_children: true
---

# Configuring Effect Providers

Effect providers handle side effects as a train runs — database writes, logging, serialization. Each provider is independent: add or remove any of them without changing your train code. For the conceptual background, see [Effect overview]({{ site.baseurl }}{% link effect.md %}).

## Database Persistence (Postgres or InMemory)

**Use when:** You need to query train history, audit execution, or debug production issues.

```csharp
// Production
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=app;Username=postgres;Password=pass")
    )
);

// Testing
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
);
```

*SDK Reference: [UsePostgres]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}), [UseInMemory]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %})*

This persists a `Metadata` record for each train execution containing:
- Train name and state (Pending -> InProgress -> Completed/Failed)
- Start and end timestamps
- Serialized input and output
- Exception details if failed
- Parent train ID for nested trains

See [Data Persistence](effect-providers/data-persistence.md) for the full breakdown of both backends, what gets persisted, and DataContext logging.

## JSON Effect (`AddJson`)

**Use when:** Debugging during development. Logs train state changes to your configured logger.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJson()
    )
);
```

*SDK Reference: [AddJson]({{ site.baseurl }}{% link sdk-reference/configuration/add-json-effect.md %})*

This doesn't persist anything — it just logs. Useful for seeing what's happening without setting up a database.

See [JSON Effect](effect-providers/json-effect.md) for how change detection works.

## Parameter Effect (`SaveTrainParameters`)

**Use when:** You need to store train inputs/outputs in the database for later querying or replay.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()  // Serializes Input/Output to Metadata
    )
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

## Junction Logger (`AddJunctionLogger`)

**Use when:** You want structured logging for individual junction executions inside a train.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionLogger(serializeJunctionData: true)
    )
);
```

*SDK Reference: [AddJunctionLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-junction-logger.md %})*

This hooks into `EffectJunction` (not base `Junction`) lifecycle events. Before and after each junction runs, it logs structured `JunctionMetadata` containing the junction name, input/output types, timing, and Railway state (`Right`/`Left`). When `serializeJunctionData` is `true`, the junction's output is also serialized to JSON in the log entry.

Requires junctions to inherit from `EffectJunction<TIn, TOut>` instead of `Junction<TIn, TOut>`. See [EffectJunction vs Junction]({{ site.baseurl }}{% link core/trains-and-junctions.md %}#effectjunction-vs-junction).

See [Junction Logger](effect-providers/junction-logger.md) for the full JunctionMetadata field reference.

## Junction Progress & Cancellation Check (`AddJunctionProgress`)

**Use when:** You need per-junction progress visibility in the dashboard and/or the ability to cancel running trains from the dashboard (including cross-server cancellation).

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionProgress()
    )
);
```

*SDK Reference: [AddJunctionProgress]({{ site.baseurl }}{% link sdk-reference/configuration/add-junction-progress.md %})*

This registers two junction-level effect providers:

1. **CancellationCheckProvider** — Before each junction, queries the database for `Metadata.CancellationRequested`. If `true`, throws `OperationCanceledException`, which maps to `TrainState.Cancelled`.
2. **JunctionProgressProvider** — Before each junction, writes the junction name and start time to `Metadata.CurrentlyRunningJunction` and `Metadata.JunctionStartedAt`. After the junction, clears both columns.

The cancellation check runs first so a cancelled train never writes progress columns for a junction that won't execute. Requires junctions to inherit from `EffectJunction<TIn, TOut>`.

See [Junction Progress](effect-providers/junction-progress.md) for the dual-path cancellation architecture and dashboard integration.

## Lifecycle Hooks (`AddLifecycleHook`)

**Use when:** You want side effects to fire on train state transitions — notifications, metrics, real-time updates — without coupling your train code to those concerns.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddLifecycleHook<SlackNotificationHookFactory>()
    )
);
```

*SDK Reference: [AddLifecycleHook]({{ site.baseurl }}{% link sdk-reference/configuration/add-lifecycle-hook.md %})*

Lifecycle hooks implement `ITrainLifecycleHook` and fire at four points: `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled`. Unlike effect providers, hook exceptions are **caught and logged, never propagated** — a failing Slack webhook will never cause a train to fail.

The `Trax.Api.GraphQL` package includes a built-in hook (`GraphQLSubscriptionHook`) that publishes lifecycle events to [GraphQL subscriptions]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}) over WebSocket. It's automatically registered by `AddTraxGraphQL()`. Only trains decorated with [`[TraxBroadcast]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-subscription-attribute.md %}) have their events published.

## Combining Providers

Providers compose. A typical production setup:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)             // Persist metadata
        .SaveTrainParameters()                     // Include input/output in metadata
        .AddJunctionLogger(serializeJunctionData: true)    // Log individual junction executions
        .AddJunctionProgress()                         // Junction progress + cancellation check
    )
    .AddMediator(assemblies)                       // Enable train discovery
);
```

A typical development setup:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()                             // Fast, no database needed
        .AddJson()                                 // Log state changes
        .AddJunctionLogger()                           // Log junction executions
    )
    .AddMediator(assemblies)
);
```
