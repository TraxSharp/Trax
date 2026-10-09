---
layout: default
title: IDataContext
description: "Reference for IDataContext, Trax's EF Core context over the trax tables: its tables and members, how to get one, and committing writes with an enqueue."
parent: Configuration
grand_parent: SDK Reference
nav_order: 18
---

# IDataContext

Trax's own data context: an EF Core context over the `trax` tables (runs, logs, manifests, the work queue, dead letters and the rest), and the effect provider that persists a train's metadata. [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) and [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) each register an implementation.

Inject it to read Trax's tables, or to make a write commit with an enqueue through [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor). Your own tables belong in a [domain data context](/docs/sdk-reference/configuration/domain-data-context), not here.

## Signature

```csharp
namespace Trax.Effect.Data.Services.DataContext;

public interface IDataContext : IEffectProvider, IAsyncDisposable  // IEffectProvider is IDisposable
{
    DbSet<Metadata> Metadatas { get; }
    DbSet<Log> Logs { get; }
    DbSet<Manifest> Manifests { get; }
    DbSet<ManifestGroup> ManifestGroups { get; }
    DbSet<WorkQueue> WorkQueues { get; }
    DbSet<DeadLetter> DeadLetters { get; }
    DbSet<BackgroundJob> BackgroundJobs { get; }
    DbSet<SchedulerConfig> SchedulerConfigs { get; }
    DbSet<PersistedOperation> PersistedOperations { get; }
    DbSet<PersistedOperationHistory> PersistedOperationHistories { get; }
    DbSet<RunnerNonce> RunnerNonces { get; }
    DbSet<RecordedDecision> RecordedDecisions { get; }
    DbSet<JunctionRun> JunctionRuns { get; }
    DbSet<SnapshotDraft> SnapshotDrafts { get; }
    DbSet<EffectClaim> EffectClaims { get; }
    DbSet<Checkpoint> Checkpoints { get; }

    int Changes { get; set; }

    Task<IDataContextTransaction> BeginTransaction();
    Task<IDataContextTransaction> BeginTransaction(CancellationToken cancellationToken);
    Task<IDataContextTransaction> BeginTransaction(IsolationLevel isolationLevel);
    Task<IDataContextTransaction> BeginTransaction(IsolationLevel isolationLevel, CancellationToken cancellationToken);
    Task CommitTransaction();
    Task RollbackTransaction();
    void Reset();
    DataContext<TDbContext> Raw<TDbContext>() where TDbContext : DbContext;
}

namespace Trax.Effect.Data.Services.DataContextTransaction;

public interface IDataContextTransaction : IDisposable
{
    Task Commit();
    Task Rollback();
}
```

From `IEffectProvider` it also has `Track(IModel)`, `Update(IModel)` and `SaveChanges(CancellationToken)`, which the effect runner uses to persist a run.

## Tables

| Property | Table | Holds |
|----------|-------|-------|
| `Metadatas` | `trax.metadata` | One row per train run: state, input, output, timing, failure |
| `Logs` | `trax.log` | Log entries written by [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging) |
| `Manifests` | `trax.manifest` | Scheduled jobs: train, input properties, schedule |
| `ManifestGroups` | `trax.manifest_group` | Groups of manifests that share dispatch settings |
| `WorkQueues` | `trax.work_queue` | Runs waiting to be dispatched |
| `DeadLetters` | `trax.dead_letter` | Manifests that exhausted their retries |
| `BackgroundJobs` | `trax.background_job` | Dispatched runs waiting for, or held by, a worker; only in-flight work |
| `SchedulerConfigs` | `trax.scheduler_config` | The persisted, dashboard-editable scheduler settings; zero or one row |
| `PersistedOperations`, `PersistedOperationHistories` | `trax.persisted_operation`, `trax.persisted_operation_history` | Persisted GraphQL operations and their audit history |
| `RunnerNonces` | `trax.runner_nonce` | Nonces a runner accepted on signed requests |
| `RecordedDecisions` | `trax.decision` | Each decision a run made, written by [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) |
| `JunctionRuns` | `trax.junction_run` | Each step of a run, written by [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events). Read one run's steps in order with `ForRun(metadataId)`. |
| `SnapshotDrafts` | `trax.snapshot_draft` | State-machine drafts, one per user and draft id |
| `EffectClaims` | `trax.effect_claim` | Exactly-once state-machine effect intents |
| `Checkpoints` | `trax.checkpoint` | The state a train declared with `Checkpoint<TState>()`, one row per run and node, from which a later run resumes. |

A `Checkpoint` row holds the run (`MetadataId`), the declared node (`NodeId`, and `BranchPath` inside a
`Parallel` branch), the stored state as JSON (`State`) with its type (`StateType`), the tracks the routing steps
before it took (`Tracks`), and the two values a resume compares with the running code: `ChainHash`, the declared
chain's hash, and `StateFingerprint`, a fingerprint of the state type's serialized shape. A resume refuses when
either differs. `CreatedAt` is when it was written. See [Checkpoint](/docs/sdk-reference/train-methods/checkpoint).

`RunnerNonces`, `RecordedDecisions`, `JunctionRuns`, `SnapshotDrafts`, `EffectClaims` and `Checkpoints` have default implementations on the interface, so an implementation written before they existed still loads.

## Members

| Member | Description |
|--------|-------------|
| `BeginTransaction(...)` | Starts a transaction, optionally at an isolation level. Returns an `IDataContextTransaction` to commit, roll back or dispose. |
| `CommitTransaction()` / `RollbackTransaction()` | Commits or rolls back the context's current transaction |
| `Reset()` | Clears the change tracker, so a long-lived context stops tracking what it loaded |
| `Changes` | A count of tracked changes not yet saved |
| `Raw<TDbContext>()` | Casts to the concrete `DataContext<TDbContext>`, for EF members the interface does not surface. Throws `InvalidCastException` when the implementation is not that type. |

## Getting one

| How | Lifetime | Use |
|-----|----------|-----|
| Inject `IDataContext` | Scoped: one per scope, created from the provider's context factory | Reads and writes inside a request or a train's scope |
| Inject `IDataContextProviderFactory` and call `CreateDbContextAsync(ct)` | A new context per call; dispose it | Background services and anything outside a scope |

## Example

```csharp
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

public class FailedRunReport(IDataContext data)
{
    public Task<List<string>> TrainsFailingSince(DateTime sinceUtc, CancellationToken ct) =>
        data.Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Failed && m.EndTime > sinceUtc)
            .Select(m => m.Name)
            .Distinct()
            .ToListAsync(ct);
}
```

Treat the `trax` tables as read-only from application code. Trax's services write them, and a row changed behind their back (a run's state, a work queue entry's status) can break the scheduler's invariants.

## Package

```
dotnet add package Trax.Effect.Data
```

A data provider package (`Trax.Effect.Data.Postgres`, `Trax.Effect.Data.Sqlite` or `Trax.Effect.Data.InMemory`) brings it in.
