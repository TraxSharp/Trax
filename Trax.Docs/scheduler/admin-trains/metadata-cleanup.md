---
title: MetadataCleanupTrain
description: "The MetadataCleanup train that deletes old Metadata rows for internal and chosen trains: how it runs, deletion order, safety boundary and per-train retention."
source:
  - Trax.Scheduler/src/Trax.Scheduler/Trains/MetadataCleanup
type: reference
area: scheduling
nav_order: 6
---

# MetadataCleanupTrain

The MetadataCleanup train deletes old metadata rows for the internal scheduler trains. Every train execution persists a metadata row, so high-frequency internal trains like JobDispatcher (every 2 seconds by default) and ManifestManager (every 5 seconds) would otherwise generate hundreds of thousands of rows per day. On a database provider the cleanup always runs, and the internal scheduler trains are pruned unconditionally; see [Configuration](#configuration). Cancelled runs are terminal like completed and failed ones, so they are swept too.

## Chain

```
DeleteExpiredMetadataJunction
```

One junction. It's a simple train because the logic is straightforward, the complexity is in the deletion query, not in orchestration.

## How It Runs

The `MetadataCleanupPollingService` is a separate `BackgroundService` from the manifest polling service. It runs on its own interval (`CleanupInterval`, default: 1 minute) and invokes the MetadataCleanupTrain each cycle. It also runs a cleanup immediately on startup.

## The Deletion Junction

`DeleteExpiredMetadataJunction` deletes expired metadata in configurable batches (default: 1000 rows per batch) to limit row-level lock duration. Each batch loads metadata IDs first, deletes the owned child rows (work queue entries, logs), clears the back-references that would otherwise block the delete (dead letter retry links, child metadata parent links), then deletes the metadata by ID. The junction loops until no more expired rows remain.

A metadata row is deleted when all three conditions are true:

1. Its `Name` matches an internal scheduler train (always eligible) or a train in the `TrainTypeWhitelist`
2. Its `StartTime` is older than `RetentionPeriod`
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

`Pending` and `InProgress` metadata is never deleted, regardless of age. Only terminal states are eligible.

## Concurrency Model: Idempotent Bulk Deletes

The MetadataCleanup train uses no application-level locking. Multiple servers can run cleanup concurrently without conflict because the operations are inherently idempotent.

### Implicit Database Locks

`DeleteExpiredMetadataJunction` uses EF Core's `ExecuteDeleteAsync()`, which translates to atomic `DELETE FROM ... WHERE ...` SQL statements. The database engine acquires implicit row-level locks during these deletes. If two servers execute the same `DELETE` concurrently, the first deletes the rows and the second finds no matching rows, a no-op. No errors, no side effects.

Batching limits the duration of these implicit row-level locks. A single `DELETE` matching thousands of rows would hold locks on all of them for the entire statement, so there is no unbounded setting: `DeleteBatchSize` is 1 to 10,000, and null sweeps in batches of 10,000. Each batch locks about `DeleteBatchSize` runs and what they own, which keeps contention with concurrent workers calling `SaveChanges` on the same table short.

### Deletion Order

Each batch follows this process:

1. **Load batch of metadata IDs**: select up to `DeleteBatchSize` IDs matching the criteria, then add the expired runs each must be deleted with (below)
2. **Begin a transaction**: the steps up to the metadata delete run in one transaction, all or nothing
3. **WorkQueue entries**: delete entries whose `MetadataId` is in the batch
4. **Log entries**: delete logs whose `MetadataId` is in the batch
5. **Clear back-references**: null `dead_letter.retry_metadata_id` and child `metadata.parent_id` pointing at the batch, so the `ON DELETE RESTRICT` foreign keys don't block the delete (the dead letter and any still-running child are meaningful records and are kept)
6. **Metadata rows**: delete metadata by ID, repeating the replay checks below. When a run in the batch was linked for replay after it was selected, the delete keeps it and the transaction rolls back, so the run keeps everything it owns; the batch is rechecked and tried again without it. After three attempts the batch is logged as a warning and left for a later sweep
7. **Repeat** until no more IDs match

Using ID-based deletion guarantees the statements in a batch target the exact same rows, avoiding race conditions between statements.

A batch that still fails (for example an unexpected foreign-key reference or a deadlock) is bisected to isolate the offending row, which is logged and skipped so one bad row can never abort the whole sweep. Skipped rows are excluded from later batches and retried on the next cleanup cycle.

### Safety Boundary

Only metadata in a **terminal state** (`Completed`, `Failed`, or `Cancelled`) is eligible for deletion. `Pending` and `InProgress` metadata is never deleted regardless of age, so cleanup cannot interfere with in-flight executions.

Runs linked by [decision replay](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions) (`replay_decisions_of`) are kept together:

- A run is kept while a queued work queue entry, or a run that is kept, names it, because something still replays it.
- A run that replays another is never deleted while the run it replays is kept. Both expired, they are deleted in the same batch and transaction, so a batch can hold more runs than `DeleteBatchSize`. A replay whose source stays is kept with it.

Runs linked by a checkpoint resume (`resume_from`) are kept together the same way, since a run's
checkpoints are deleted with it: a run is kept while a queued entry or a kept run resumes it, and a
resumed run and the run it resumed are deleted together once both have expired. A work queue entry
that is no longer queued (cancelled, or dispatched as a run of its own) and names a deleted run has
its `resume_from` cleared, so no row names a run that is gone. A manifest's prune clears the link on
a run or entry outside the manifest, such as an operator's resume, that names one of the manifest's
runs; a queued one then runs from the top.

So no run is ever kept while a replay of it has been deleted, and a retry's check that [nothing else replays the failed run](/docs/scheduler/how-retries-work#retries-replay-decisions) reads the same history on every host.

See [Multi-Server Concurrency](/docs/scheduler/concurrency) for the full cross-service concurrency model.

## Configuration

A scheduler with `UsePostgres()` or `UseSqlite()` runs the cleanup on its defaults without being asked. `.AddMetadataCleanup()` changes them:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.AddMetadataCleanup()
        )
);
```

With no arguments, as with no call at all, this cleans up metadata older than 30 minutes, checking every minute. The internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) are always cleaned up, you can't forget one, and the retention window still keeps the last 30 minutes for diagnostics. Use the configure action to add your own noisy trains on top.

### Your own trains, and a retention per train

`AddTrainType<T>()` adds a train of your own to the sweep, matched by `typeof(T).FullName`
against the metadata's `Name` column (the interface's full name); a string overload takes a raw
name. Given a `TimeSpan`, the train keeps its own retention:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.AddMetadataCleanup(cleanup =>
            {
                cleanup.RetentionPeriod = TimeSpan.FromMinutes(30); // everything else
                cleanup.AddTrainType<ISyncTrain>(); // on the default
                cleanup.AddTrainType<IReportTrain>(TimeSpan.FromDays(30));
            })
        )
);
```

Trains sharing a cutoff are swept together, so the cleanup runs its batched delete once per
distinct retention, and `DeleteBatchSize` is the limit per group. The work queue rows a metadata
row owns follow it, so they inherit its train's cutoff. The options and their defaults are on
[AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup).

> **The runtime override applies to the default only.** The retention is also editable without a restart, through the dashboard's Server Settings page and the `updateScheduler` mutation. That edit replaces `RetentionPeriod`, so it moves every train that is on the default and leaves a retention passed to `AddTrainType` exactly where it was. A per-train value is usually there because of what the row holds rather than as an operational preference, and an edit to a field labelled "Retention Period" should not quietly shorten it. Shortening one is a code change.

### What cannot take its own retention

The internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) are always swept at `RetentionPeriod`. Passing a retention for one throws at configuration time rather than being ignored, because they are the highest-volume writers in the table and lengthening one is the unbounded growth the unconditional sweep exists to prevent.

### Declaring a train twice

A train declared twice with different retentions is refused, in one of two places depending on how it was written:

- **The same name twice** is caught immediately by `AddTrainType`, which throws.
- **The interface name and the class name**, which are the same train, are two unrelated strings until the train registry relates them. That is not possible while the builder is still running, so it is caught at startup instead and the host refuses to start.

Declaring the same train twice with the *same* retention is fine, and is not counted twice.

If the startup check is somehow bypassed, the sweep keeps the longer of the two retentions and logs a warning. It does not throw: a cleanup loop that fails every cycle stops pruning altogether, which is worse than the misconfiguration.

## SDK Reference

> [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup)
