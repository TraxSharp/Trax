---
title: IDataContext
description: "Reference for IDataContext, Trax's EF Core context over the trax tables: its tables and members, how to get one, and committing writes with an enqueue."
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 40
package: Trax.Effect.Data
namespace:
  - Trax.Effect.Data.Services.DataContext
  - Trax.Effect.Data.Services.DataContextTransaction
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContext/IDataContext.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContextTransaction/IDataContextTransaction.cs
---

# IDataContext

Trax's own data context: an EF Core context over the `trax` tables (runs, logs, manifests, the work queue, dead letters and the rest), and the effect provider that persists a train's metadata. [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) and [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) each register an implementation.

It is what [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) takes to make a write commit with an enqueue. Application tables live in a [domain data context](/docs/sdk-reference/configuration/domain-data-context).

## Signature

```csharp fragment
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
    DbSet<SnapshotTransition> SnapshotTransitions { get; }

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

## Members

### Tables

| Property | Table | Holds |
|----------|-------|-------|
| `Metadatas` | `trax.metadata` | One row per train run: state, input, output, timing, failure |
| `Logs` | `trax.log` | Log entries written by [AddDataContextLogging](/docs/sdk-reference/configuration/add-data-context-logging) |
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
| `SnapshotTransitions` | `trax.snapshot_transition` | Each state-machine instance's history: one row per snapshot write, deleted with the instance |

A `Checkpoint` row holds the run (`MetadataId`), the declared node (`NodeId`, and `BranchPath` inside a
`Parallel` branch), the stored state as JSON (`State`) with its type (`StateType`), the tracks the routing steps
before it took (`Tracks`), and the two values a resume compares with the running code: `ChainHash`, the declared
chain's hash, and `StateFingerprint`, a fingerprint of the state type's serialized shape. A resume refuses when
either differs. `CreatedAt` is when it was written. See [Checkpoint](/docs/sdk-reference/train-methods/checkpoint).

A `SnapshotTransition` row is one write of an instance's snapshot: the instance's row (`DraftRowId`), the `Revision`
the write gave it, its `Kind` (`SnapshotTransitionKind`: `Start`, `Command`, `Save`, `Timer` or `Outcome`), the
command's name (`Trigger`), the `Actor` (`SnapshotTransitionActor`: `Owner`, `Host` or `Machine`), `FromState` and
`ToState`, when it was written (`At`), and the server-only `InvokeToken` of the run or child it started. The store
writes it in the snapshot's transaction; it holds no context and no owner key. Operators read it as
[`machineInstance { transitions }`](/docs/sdk-reference/graphql-api/queries/machine-instances#transitions).

`RunnerNonces`, `RecordedDecisions`, `JunctionRuns`, `SnapshotDrafts`, `EffectClaims`, `Checkpoints` and `SnapshotTransitions` have default implementations on the interface, so an implementation written before they existed still loads.

### Methods

| Member | Description |
|--------|-------------|
| `BeginTransaction(...)` | Starts a transaction, optionally at an isolation level. Returns an `IDataContextTransaction` to commit, roll back or dispose. |
| `CommitTransaction()` / `RollbackTransaction()` | Commits or rolls back the context's current transaction |
| `Reset()` | Clears the change tracker, so a long-lived context stops tracking what it loaded |
| `Changes` | A count of tracked changes not yet saved |
| `Raw<TDbContext>()` | Casts to the concrete `DataContext<TDbContext>`, for EF members the interface does not surface. Throws `InvalidCastException` when the implementation is not that type. |

## Behavior

### Getting one

| How | Lifetime | Use |
|-----|----------|-----|
| Inject `IDataContext` | Scoped: one per scope, created from the provider's context factory | Reads and writes inside a request or a train's scope |
| Inject `IDataContextProviderFactory` and call `CreateDbContextAsync(ct)` | A new context per call; dispose it | Background services and anything outside a scope |

Trax's services write the `trax` tables. A row changed outside them (a run's state, a work queue entry's status)
can break the scheduler's invariants.

## Example

```csharp compile
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

## See also

- [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite), [UseInMemory](/docs/sdk-reference/configuration/use-in-memory)
- [Data Persistence](/docs/effect/effect-providers/data-persistence)
