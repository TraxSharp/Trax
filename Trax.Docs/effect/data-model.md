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
