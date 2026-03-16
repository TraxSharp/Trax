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

One junction. It's a simple train because the logic is straightforward—the complexity is in the deletion query, not in orchestration.

## How It Runs

The `MetadataCleanupPollingService` is a separate `BackgroundService` from the manifest polling service. It runs on its own interval (`CleanupInterval`, default: 1 minute) and invokes the MetadataCleanupTrain each cycle. It also runs a cleanup immediately on startup.

## The Deletion Junction

`DeleteExpiredMetadataJunction` uses EF Core's `ExecuteDeleteAsync` for efficient bulk deletion—no entities are loaded into memory. It deletes in two passes to respect foreign key constraints:

1. **Delete log entries** for matching metadata rows
2. **Delete metadata rows** themselves

A metadata row is deleted when all three conditions are true:

1. Its `Name` matches a train in the `TrainTypeWhitelist`
2. Its `StartTime` is older than `RetentionPeriod`
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

`Pending` and `InProgress` metadata is never deleted, regardless of age. Only terminal states are eligible.

## Concurrency Model: Idempotent Bulk Deletes

The MetadataCleanup train uses no application-level locking. Multiple servers can run cleanup concurrently without conflict because the operations are inherently idempotent.

### Implicit Database Locks

`DeleteExpiredMetadataJunction` uses EF Core's `ExecuteDeleteAsync()`, which translates to atomic `DELETE FROM ... WHERE ...` SQL statements. The database engine acquires implicit row-level locks during these deletes. If two servers execute the same `DELETE` concurrently, the first deletes the rows and the second finds no matching rows — a no-op. No errors, no side effects.

### Deletion Order

The junction deletes in a specific order to respect foreign key constraints:

1. **WorkQueue entries** — delete entries whose `MetadataId` matches the set to be cleaned
2. **Log entries** — delete logs whose `MetadataId` matches
3. **Metadata rows** — delete the metadata itself

This ordering prevents FK constraint violations. Each `ExecuteDeleteAsync` is its own SQL statement (not wrapped in an explicit transaction), so a failure in pass 2 would leave orphaned WorkQueue deletions — but since the Metadata rows survive, the next cleanup cycle will retry and complete the deletion.

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

*SDK Reference: [AddMetadataCleanup]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-metadata-cleanup.md %})*

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
| `TrainTypeWhitelist` | ManifestManager, MetadataCleanup | Train names whose metadata can be deleted |
