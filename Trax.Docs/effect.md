---
title: Trax.Effect
description: "How Trax.Effect works: ServiceTrain, the record it writes for every run, junctions resolved from DI, and the effect pattern behind its providers."
type: explanation
area: effects
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
  - Trax.Effect/src/Trax.Effect/Services/EffectRunner/EffectRunner.cs
---

# Trax.Effect

`Trax.Effect` gives every train run a record: a row with its state, start and end time, the junction that failed and the exception it threw, stored in PostgreSQL, SQLite or memory. It also resolves junctions from dependency injection and runs pluggable effect providers around each run.

It adds to [Core](/docs/core):

- **`ServiceTrain<TIn, TOut>`**, a `Train` that records its own runs.
- **The run record**: every run produces a queryable row (state, timing, input and output when saved, errors). [Metadata](/docs/effect/metadata) lists its fields.
- **Dependency injection**: junctions are resolved from your container.
- **Effect providers**: pluggable providers for persistence, logging and serialization. [Choose effect providers](/docs/effect/effect-providers) lists them.
- **Lifecycle hooks**: fire on run state transitions (started, completed, failed, cancelled) for notifications, metrics or real-time updates.

## Train and ServiceTrain

`Train<TIn, TOut>` (Core) is the pipeline engine. It chains junctions, carries the failure track and manages Memory. It does no logging, no DI and no side effects.

`ServiceTrain<TIn, TOut>` is a `Train` with services around it. Its `Run` is sealed, and wraps every run with:

- a run record (start time, end time, success or failure, and the input and output when parameter saving is on)
- the registered effect providers (database persistence, JSON logging, parameter serialization)
- the junction effects and lifecycle hooks
- the `IServiceProvider` the run resolves its junctions from

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateUserJunction>()
            .Chain<CreateUserJunction>().Resolve();
}
```

The code inside `Junctions()` is the same as on a `Train`. `ServiceTrain` adds what runs around it. Each call to `Run` writes its own row, so an instance runs one execution at a time, which is why a service train cannot be registered as a singleton.

## The effect pattern

Effects are the work a run causes outside the train: rows written, logs emitted, parameters serialized. Junctions do not write to a database or logger directly. The train tracks models during the run, and the effect providers do the work when the run ends:

- On both tracks, every provider runs `SaveChanges`, and the run's metadata (state, timing, errors) is persisted.
- When the train reaches the success track, its output is recorded with the metadata.
- When a junction takes the failure track, the exception and failure details are recorded, and the same `SaveChanges` runs on every provider. Nothing rolls back: whatever the train tracked before it failed, including entities added to the train's `DataContext` and models a custom provider holds, is committed with the failure record.

Each provider saves in turn, and one that throws does not stop the others. The run's outcome is persisted whatever a provider did.

So a provider can be added or removed without touching train code, because the train never names one. Remove a provider and the train still runs; nothing records what that provider recorded. And a failed run leaves a full record rather than nothing, because the write is part of how the run ends on both tracks, not part of the success path.

The alternative, junctions writing their own side effects as they go, puts a database call or a logger in every junction, and a failure halfway leaves the run's record in whatever state the failing junction left it.

## Setting it up

The [Getting Started](/docs/getting-started#4-record-runs-in-postgres) tutorial adds Trax.Effect to a project and records runs in Postgres. [AddTrax and AddEffects](/docs/sdk-reference/configuration) has the registration calls, and each provider's page has its own.

When callers should not know which train handles a request, add [Trax.Mediator](/docs/mediator).

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
