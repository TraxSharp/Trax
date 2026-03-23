---
layout: default
title: Effect
nav_order: 4
has_children: true
section: Packages
---

# Trax.Effect

`Trax.Effect` upgrades the core pipeline engine into a full commercial service — with journey logging, dependency injection, and pluggable effect providers.

```bash
dotnet add package Trax.Effect
dotnet add package Trax.Effect.Data.Postgres  # or Trax.Effect.Data.InMemory
```

## What It Adds

Everything in [Core](core.md), plus:

- **`ServiceTrain<TIn, TOut>`** — extends `Train` with automatic metadata tracking
- **Execution metadata** — every train run produces a queryable record (state, timing, input/output, errors)
- **Dependency injection** — junctions resolved from your DI container
- **Effect providers** — pluggable providers for persistence, logging, and serialization
- **Lifecycle hooks** — fire on train state transitions (started, completed, failed, cancelled) for notifications, metrics, or real-time updates
- **Atomic side effects** — all providers save together; metadata (state, timing, errors) is always persisted regardless of outcome

## Train vs ServiceTrain

**`Train<TIn, TOut>`** (Core) — The core pipeline engine. Chains junctions, propagates errors, manages Memory. No logging, no DI, no side effects.

**`ServiceTrain<TIn, TOut>`** (Effect) — The full commercial service. Wraps every journey with:
- Journey logging (departure time, arrival time, right track / left track, cargo in/out)
- Effect providers (database persistence, JSON logging, parameter serialization)
- Integration with `ITrainBus` for dispatch discovery
- `IServiceProvider` access for junction instantiation

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override User Junctions() =>
        Chain<ValidateUserJunction>()
            .Chain<CreateUserJunction>();
}
```

The code inside `Junctions()` is identical — `ServiceTrain` adds the infrastructure around it.

## The Effect Pattern

Effects are operations that happen as the train passes through its route — provided by pluggable effect providers. Junctions don't write directly to a database or logger. Instead, the train tracks models during the journey, and effect providers handle the actual work at the end:

- On both tracks, effect providers run `SaveChanges` — metadata (state, timing, errors) is always persisted
- If the train reaches the right track (success), output is recorded alongside the metadata
- If any junction takes the left track (failure), the exception and failure details are recorded. User-tracked models added via custom effect providers are not committed.

This gives you full audit trails on every outcome and modularity (add/remove providers without changing train code).

## Setup

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)          // Persist metadata (returns TraxEffectBuilderWithData)
        .SaveTrainParameters()                  // Include input/output in metadata
        .AddJunctionLogger(serializeJunctionData: true) // Log individual junction executions
    )
);
```

The `AddEffects` callback is a `Func<TraxEffectBuilder, TraxEffectBuilder>` — the lambda returns the builder from the last chained call. Data provider methods (`UsePostgres`, `UseInMemory`) return `TraxEffectBuilderWithData`, which unlocks data-dependent methods like `AddDataContextLogging` at compile time.

Remove any line and the train still runs — it just passes through fewer junctions.

## When to Use Effect

- Web APIs where you need to know what ran and why it failed
- Services that need execution audit trails
- Any application where you want observability without building custom logging

When you need decoupled dispatch (callers don't know which train handles a request), add [Trax.Mediator](mediator.md).

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
