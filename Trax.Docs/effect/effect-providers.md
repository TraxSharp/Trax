---
title: Choose effect providers
description: "Pick the effect providers a host registers (persistence, JSON, parameters, junction logger and progress, lifecycle hooks) and combine them."
type: how-to
area: effects
nav_order: 1
---

# Choose effect providers

Effect providers do the side effects of a run: database writes, logging, serialization. Each one is independent, so adding or removing one does not change train code. Why they work this way is on [Effect](/docs/effect#the-effect-pattern).

## Pick the providers

| Provider | Call | Package | Pick it to |
|---|---|---|---|
| [Data persistence](/docs/effect/effect-providers/data-persistence) | `UsePostgres`, `UseSqlite`, `UseInMemory` | `Trax.Effect.Data.Postgres`, `.Sqlite`, `.InMemory` | Keep a queryable row for every run. Postgres or Sqlite in production, InMemory in tests. |
| [JSON effect](/docs/effect/effect-providers/json-effect) | `AddJson` | `Trax.Effect.Provider.Json` | Log each run's state changes as JSON, with no database |
| [Parameter effect](/docs/effect/effect-providers/parameter-effect) | `SaveTrainParameters` | `Trax.Effect.Provider.Parameter` | Store each run's input and output as JSON on its row |
| [Junction logger](/docs/effect/effect-providers/junction-logger) | `AddJunctionLogger` | `Trax.Effect.JunctionProvider.Logging` | Log a structured entry for every junction of a run |
| [Junction progress](/docs/effect/effect-providers/junction-progress) | `AddJunctionProgress` | `Trax.Effect.JunctionProvider.Progress` | Show the running junction on the dashboard, and stop a run between junctions when it is cancelled |
| [Lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook) | `AddLifecycleHook<T>` | `Trax.Effect` | Run code of your own when a run starts, completes, fails or is cancelled |

The junction logger and junction progress run around `EffectJunction<TIn, TOut>` only, not a plain `Junction<TIn, TOut>` ([EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction)). A lifecycle hook that throws is caught and logged; it never fails the run.

Other calls on the same builder record or carry more than the run row: [junction events](/docs/effect/junction-events) (`AddJunctionEvents`), [decision records](/docs/effect/decisions) (`AddDecisionRecording`) and [broadcasters](/docs/effect/broadcaster-sinks) (`UseBroadcaster`). [Host tracking](/docs/effect/host-tracking) is on by default.

## Combine them

Providers compose on one `AddEffects` call. A production host:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)                     // Persist run rows
        .SaveTrainParameters()                             // Input and output on the row
        .AddJunctionLogger(serializeJunctionData: true)    // Log each junction
        .AddJunctionProgress()                             // Junction progress and cancellation check
    )
    .AddMediator(assemblies)
);
```

A development host, with no database:

```csharp compile
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
        .AddJson()                                         // Log state changes
        .AddJunctionLogger()
    )
    .AddMediator(assemblies)
);
```

The `AddEffects` lambda returns the builder from its last call. A data provider returns `TraxEffectBuilderWithData`, which is what the data-dependent calls (`AddDataContextLogging`, `AddDecisionRecording`, `AddJunctionEvents`) need, so they come after it. [AddTrax and AddEffects](/docs/sdk-reference/configuration) has the builder types.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [AddJson](/docs/sdk-reference/configuration/add-json) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
