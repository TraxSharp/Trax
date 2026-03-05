---
layout: default
title: Effect
nav_order: 4
has_children: true
---

# Trax.Effect

`Trax.Effect` upgrades a bare train into a full commercial service — with journey logging, dependency injection, and pluggable station services (effect providers).

```bash
dotnet add package Trax.Effect
dotnet add package Trax.Effect.Data.Postgres  # or Trax.Effect.Data.InMemory
```

## What It Adds

Everything in [Core](core.md), plus:

- **`ServiceTrain<TIn, TOut>`** — extends `Train` with automatic metadata tracking
- **Execution metadata** — every train run produces a queryable record (state, timing, input/output, errors)
- **Dependency injection** — steps resolved from your DI container
- **Effect providers** — pluggable station services for persistence, logging, and serialization
- **Lifecycle hooks** — fire on train state transitions (started, completed, failed, cancelled) for notifications, metrics, or real-time updates
- **Atomic side effects** — all providers save together on success; nothing is saved on failure

## Train vs ServiceTrain

**`Train<TIn, TOut>`** (Core) — The bare locomotive. Chains steps, propagates errors, manages Memory. No logging, no DI, no side effects.

**`ServiceTrain<TIn, TOut>`** (Effect) — The full commercial service. Wraps every journey with:
- Journey logging (departure time, arrival time, success/derailment, cargo in/out)
- Station services (database persistence, JSON logging, parameter serialization)
- Integration with `ITrainBus` for dispatch station discovery
- `IServiceProvider` access for step instantiation

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateUserStep>()
            .Chain<CreateUserStep>()
            .Resolve();
}
```

The code inside `RunInternal` is identical — `ServiceTrain` adds the infrastructure around it.

## The Effect Pattern

Effects are station services — operations that happen as the train passes through its route. Steps don't write directly to a database or logger. Instead, the train tracks models during the journey, and effect providers handle the actual work at the end:

- If the train arrives, all providers run `SaveChanges` and services are applied atomically
- If any step derails the train, nothing is saved

This gives you atomicity (no half-written records) and modularity (add/remove providers without changing train code).

## Setup

```csharp
builder.Services.AddTraxEffects(options => options
    .AddPostgresEffect(connectionString)    // Persist metadata
    .SaveTrainParameters()                  // Include input/output in metadata
    .AddStepLogger(serializeStepData: true) // Log individual step executions
);
```

Remove any line and the train still runs — it just passes through fewer stations.

## When to Use Effect

- Web APIs where you need to know what ran and why it failed
- Services that need execution audit trails
- Any application where you want observability without building custom logging

When you need decoupled dispatch (callers don't know which train handles a request), add [Trax.Mediator](mediator.md).
