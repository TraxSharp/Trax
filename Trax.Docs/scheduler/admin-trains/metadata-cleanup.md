---
layout: default
title: MetadataCleanup
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 4
---

# MetadataCleanupTrain

The MetadataCleanup train deletes old metadata rows for high-frequency internal trains. Without it, trains like ManifestManager (which runs every 5 seconds by default) would generate hundreds of thousands of metadata rows per day.

## Chain

```
DeleteExpiredMetadataJunction
```

One junction. It's a simple train because the logic is straightforward, the complexity is in the deletion query, not in orchestration.

## How It Runs

The `MetadataCleanupPollingService` is a separate `BackgroundService` from the manifest polling service. It runs on its own interval (`CleanupInterval`, default: 1 minute) and invokes the MetadataCleanupTrain each cycle. It also runs a cleanup immediately on startup.

## The Deletion Junction

`DeleteExpiredMetadataJunction` deletes expired metadata in configurable batches (default: 1000 rows per batch) to limit row-level lock duration. Each batch loads metadata IDs first, then deletes associated FK rows and the metadata itself by ID. The junction loops until no more expired rows remain.

A metadata row is deleted when all three conditions are true:

1. Its `Name` matches a train in the `TrainTypeWhitelist`
2. Its `StartTime` is older than `RetentionPeriod`
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

`Pending` and `InProgress` metadata is never deleted, regardless of age. Only terminal states are eligible.

## Concurrency Model: Idempotent Bulk Deletes

The MetadataCleanup train uses no application-level locking. Multiple servers can run cleanup concurrently without conflict because the operations are inherently idempotent.

### Implicit Database Locks

`DeleteExpiredMetadataJunction` uses EF Core's `ExecuteDeleteAsync()`, which translates to atomic `DELETE FROM ... WHERE ...` SQL statements. The database engine acquires implicit row-level locks during these deletes. If two servers execute the same `DELETE` concurrently, the first deletes the rows and the second finds no matching rows, a no-op. No errors, no side effects.

Batching limits the duration of these implicit row-level locks. Without batching, a single `DELETE` matching thousands of rows holds locks on all of them for the entire statement. With batching, each batch locks at most `DeleteBatchSize` rows, reducing contention with concurrent workers calling `SaveChanges` on the same table.

### Deletion Order

Each batch follows a five-step process:

1. **Load batch of metadata IDs**: select up to `DeleteBatchSize` IDs matching the criteria
2. **WorkQueue entries**: delete entries whose `MetadataId` is in the batch
3. **Log entries**: delete logs whose `MetadataId` is in the batch
4. **Metadata rows**: delete metadata by ID
5. **Repeat** until no more IDs match

Using ID-based deletion guarantees all three DELETE statements in a batch target the exact same rows, avoiding race conditions between statements. Each `ExecuteDeleteAsync` is its own SQL statement, so a failure mid-batch leaves the Metadata rows intact, the next cleanup cycle retries and completes the deletion.

### Safety Boundary

Only metadata in a **terminal state** (`Completed`, `Failed`, or `Cancelled`) is eligible for deletion. `Pending` and `InProgress` metadata is never deleted regardless of age, so cleanup cannot interfere with in-flight executions.

See [Multi-Server Concurrency](../concurrency.md) for the full cross-service concurrency model.

## Configuration

Enable cleanup with `.AddMetadataCleanup()`:

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup()
)
```

With no arguments, this cleans up `ManifestManagerTrain` and `MetadataCleanupTrain` metadata older than 30 minutes, checking every minute.

### Custom Configuration

```csharp
.AddMetadataCleanup(cleanup =>
{
    cleanup.RetentionPeriod = TimeSpan.FromHours(2);
    cleanup.CleanupInterval = TimeSpan.FromMinutes(5);
    cleanup.AddTrainType<IMyNoisyTrain>();
    cleanup.AddTrainType("LegacyTrainName");
})
```

`AddTrainType<T>()` uses `typeof(T).Name` to match the `Name` column in the metadata table. You can also pass a raw string for trains that aren't easily referenced by type.

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `CleanupInterval` | 1 minute | How often the cleanup service runs |
| `RetentionPeriod` | 30 minutes | Age threshold for deletion eligibility |
| `DeleteBatchSize` | 1000 | Max rows deleted per batch. Set to `null` for single-statement deletes |
| `TrainTypeWhitelist` | ManifestManager, MetadataCleanup | Train names whose metadata can be deleted |

## SDK Reference

> [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup)
