---
layout: default
title: MetadataCleanup
parent: Administrative Workflows
grand_parent: Scheduling
nav_order: 4
---

# MetadataCleanupWorkflow

The MetadataCleanup workflow deletes old metadata rows for high-frequency internal workflows. Without it, workflows like ManifestManager (which runs every 5 seconds by default) would generate hundreds of thousands of metadata rows per day.

## Chain

```
DeleteExpiredMetadata
```

One step. It's a simple workflow because the logic is straightforward—the complexity is in the deletion query, not in orchestration.

## How It Runs

The `MetadataCleanupPollingService` is a separate `BackgroundService` from the manifest polling service. It runs on its own interval (`CleanupInterval`, default: 1 minute) and invokes the MetadataCleanupWorkflow each cycle. It also runs a cleanup immediately on startup.

## The Deletion Step

`DeleteExpiredMetadataStep` uses EF Core's `ExecuteDeleteAsync` for efficient bulk deletion—no entities are loaded into memory. It deletes in two passes to respect foreign key constraints:

1. **Delete log entries** for matching metadata rows
2. **Delete metadata rows** themselves

A metadata row is deleted when all three conditions are true:

1. Its `Name` matches a workflow in the `WorkflowTypeWhitelist`
2. Its `StartTime` is older than `RetentionPeriod`
3. Its `WorkflowState` is `Completed` or `Failed`

`Pending` and `InProgress` metadata is never deleted, regardless of age. Only terminal states are eligible.

## Concurrency Model: Idempotent Bulk Deletes

The MetadataCleanup workflow uses no application-level locking. Multiple servers can run cleanup concurrently without conflict because the operations are inherently idempotent.

### Implicit Database Locks

`DeleteExpiredMetadataStep` uses EF Core's `ExecuteDeleteAsync()`, which translates to atomic `DELETE FROM ... WHERE ...` SQL statements. The database engine acquires implicit row-level locks during these deletes. If two servers execute the same `DELETE` concurrently, the first deletes the rows and the second finds no matching rows — a no-op. No errors, no side effects.

### Deletion Order

The step deletes in a specific order to respect foreign key constraints:

1. **WorkQueue entries** — delete entries whose `MetadataId` matches the set to be cleaned
2. **Log entries** — delete logs whose `MetadataId` matches
3. **Metadata rows** — delete the metadata itself

This ordering prevents FK constraint violations. Each `ExecuteDeleteAsync` is its own SQL statement (not wrapped in an explicit transaction), so a failure in step 2 would leave orphaned WorkQueue deletions — but since the Metadata rows survive, the next cleanup cycle will retry and complete the deletion.

### Safety Boundary

Only metadata in a **terminal state** (`Completed` or `Failed`) is eligible for deletion. `Pending` and `InProgress` metadata is never deleted regardless of age, so cleanup cannot interfere with in-flight executions.

See [Multi-Server Concurrency](../concurrency.md) for the full cross-service concurrency model.

## Configuration

Enable cleanup with `.AddMetadataCleanup()`:

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup()
)
```

*API Reference: [AddMetadataCleanup]({{ site.baseurl }}{% link api-reference/scheduler-api/add-metadata-cleanup.md %})*

With no arguments, this cleans up `ManifestManagerWorkflow`, `JobDispatcherWorkflow`, and `MetadataCleanupWorkflow` metadata older than 1 hour, checking every minute.

### Custom Configuration

```csharp
.AddMetadataCleanup(cleanup =>
{
    cleanup.RetentionPeriod = TimeSpan.FromHours(2);
    cleanup.CleanupInterval = TimeSpan.FromMinutes(5);
    cleanup.AddWorkflowType<IMyNoisyWorkflow>();
    cleanup.AddWorkflowType("LegacyWorkflowName");
})
```

`AddWorkflowType<T>()` uses `typeof(T).Name` to match the `Name` column in the metadata table. You can also pass a raw string for workflows that aren't easily referenced by type.

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `CleanupInterval` | 1 minute | How often the cleanup service runs |
| `RetentionPeriod` | 1 hour | Age threshold for deletion eligibility |
| `WorkflowTypeWhitelist` | ManifestManager, JobDispatcher, MetadataCleanup | Workflow names whose metadata can be deleted |
