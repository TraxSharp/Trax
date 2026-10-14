---
title: Trax data model
description: "The tables Trax.Effect's data providers create in the trax schema, the entity each maps to, and how manifests, runs, logs and steps relate."
type: reference
area: data
nav_order: 5
source:
  - Trax.Effect/src/Trax.Effect/Models
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations
  - Trax.Effect/src/Trax.Effect.Data.Sqlite/Migrations
---

# Trax data model

The Postgres and Sqlite data providers create these tables in the `trax` schema from their core migration set. InMemory holds the same entities without tables. Each is read through a `DbSet` on [IDataContext](/docs/sdk-reference/configuration/i-data-context).

## Tables

| Table | Entity | Holds |
|---|---|---|
| `metadata` | `Metadata` | One row per run: state, timing, input and output, errors, host. Fields on [Metadata](/docs/effect/metadata). |
| `log` | `Log` | Log lines stored by `AddDataContextLogging`, each naming the run that wrote it. Columns on [Debug across processes with the log table](/docs/effect/debug-across-processes-with-the-log-table#what-a-row-holds). |
| `manifest` | `Manifest` | A schedule definition: the train, its input, cron or interval, retry policy, timeout |
| `manifest_group` | `ManifestGroup` | Per-group dispatch controls (`MaxActiveJobs`, `Priority`, `IsEnabled`) |
| `work_queue` | `WorkQueue` | Entries between scheduling and dispatch |
| `dead_letter` | `DeadLetter` | Manifests whose runs exceeded their retry limit |
| `background_job` | `BackgroundJob` | The PostgreSQL job submission queue, with a visibility timeout |
| `scheduler_config` | `SchedulerConfig` | The scheduler settings the dashboard can edit at runtime |
| `decision` | `RecordedDecision` | Each decision a run made; columns on [Decision records](/docs/effect/decisions#the-decision-table) |
| `junction_run` | `JunctionRun` | Each step of a run stored by [junction events](/docs/effect/junction-events#stored-steps) |
| `checkpoint` | `Checkpoint` | One row per run and declared node a run can [resume](/docs/sdk-reference/train-methods/checkpoint) from |
| `effect_claim` | `EffectClaim` | An idempotency claim for an exactly-once side effect, with a lease and fence token |
| `runner_nonce` | `RunnerNonce` | Nonces a runner accepted on signed requests, so instances sharing the database accept a request once |
| `snapshot_draft` | `SnapshotDraft` | State machine instance drafts, used by `Trax.Effect.StateMachine.Persistence` |
| `persisted_operation` | `PersistedOperation` | Stored GraphQL operations served by id |
| `persisted_operation_history` | `PersistedOperationHistory` | One row per change to a persisted operation |

The Postgres provider records its applied migrations in `trax.migrations`.

## Entity members

Members of the entities above that the column lists on other pages do not cover.

| Member | Description |
|---|---|
| `BackgroundJob.FetchedAt` | When a worker claimed the job; null while it is available. A value older than the visibility timeout makes it claimable again |
| `DeadLetter.RetryMetadata` | The run created for a retry of the dead letter, once one is linked |
| `DeadLetter.MarkRetried(retryMetadataId)` | Sets the status to `Retried`, the resolution time, and the retry run's id |
| `DeadLetter.LinkRetryMetadata(retryMetadataId)` | Links the retry run's id after a requeued entry is dispatched |
| `EffectClaim.LeaseExpiresAt` | When an in-flight claim's lease ends; a claim with no receipt past it can be reclaimed |
| `Exclusion.IsExcluded(utcNow)` | Whether a UTC time falls inside the exclusion window |
| `Manifest.DependsOnManifest` | The parent manifest of a dependent manifest |
| `Manifest.FailureWindowSeconds` | How far back, in seconds, a failed run counts toward `MaxRetries`; null uses the scheduler's window. Zero and negative values are refused |
| `Manifest.PropertyType` | The type `PropertyTypeName` names, among loaded types implementing `IManifestProperties`; `Unit` when there is none. Not mapped |
| `Manifest.NameType` | The train type `Name` names, among loaded types. Not mapped |
| `Manifest.ResolvePropertyType(registeredInputTypes)` | The type `PropertyTypeName` names, chosen only from the given input types |
| `Manifest.GetPropertiesUntyped()` | `Properties` deserialized as `PropertyType` |
| `Manifest.SetProperties(properties)` | Serializes the input into `Properties` and records its type's full name in `PropertyTypeName` |
| `Manifest.SetExclusions(exclusions)` | Stores the exclusion windows as JSON |
| `SchedulerConfig.SingletonId` | `1`, the id of the table's one row |
| `SchedulerConfig.TryGetOverride<T>(name, out value)` | Reads a setting a save named from `Overrides`; false when it names none |
| `SchedulerConfig.SetOverride<T>(name, value)` | Records a setting as named by a save |
| `SchedulerConfig.RemoveOverride(name)` | Drops a setting from `Overrides`, so each host's code value applies again |
| `SchedulerConfig.OverriddenSettings` | The setting names `Overrides` holds |
| `SnapshotDraft.ConcurrencyToken` | A new `Guid` on every write, mapped as the row's concurrency token |
| `WorkQueue.MinPriority`, `WorkQueue.MaxPriority` | `0` and `31`, the range a work queue entry's priority is clamped to |

### Infrastructure types

| Type | Description |
|---|---|
| `CreateManifest` | The values `Manifest.Create` takes, `FailureWindowSeconds` (at least 1 when set) among them |
| `CreateDeadLetter` | The values `DeadLetter.Create` takes; `RetryCount` is stored as `RetryCountAtDeadLetter` |
| `CreateBackgroundJob` | The values `BackgroundJob.Create` takes |
| `CreateJunctionMetadata` | The per-junction values `JunctionMetadata.Create` combines with the run's metadata |
| `CreateLog` | One `ILogger` call, as `Log.Create` takes it for `AddDataContextLogging` |
| `UtcValueConverter` | In `Trax.Effect.Data`: marks every `DateTime` read from the database as UTC, applied to every `DateTime` property by each provider |

## Relationships

```
MANIFEST_GROUP (1:N) -> MANIFEST (1:N) -> METADATA (1:N) -> LOG
                           |                 |  (1:N) -> DECISION
                           | 1:N             |  (1:N) -> JUNCTION_RUN
                           v                 |  self-ref (ParentId)
                      DEAD_LETTER
```

A run's `decision` and `junction_run` rows are deleted with its `metadata` row by a cascading foreign key. `JunctionMetadata`, which a junction carries while it runs, is not stored in any table.

## See also

- [Effect architecture](/docs/effect/architecture)
- [Data persistence](/docs/effect/effect-providers/data-persistence)

## SDK Reference

> [IDataContext](/docs/sdk-reference/configuration/i-data-context) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite)
