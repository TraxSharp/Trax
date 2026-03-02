---
layout: default
title: Orphan Manifest Cleanup
parent: Scheduling
nav_order: 4
---

# Orphan Manifest Cleanup

When you remove a schedule definition from your startup configuration (e.g., delete a `.Schedule(...)` call from `Program.cs`), the scheduler automatically deletes the corresponding manifest and all its related data from the database on the next startup. This prevents stale manifests from continuing to fire after their code has been removed.

## How It Works

```
┌──────────────────────────────────────────────────────────────────┐
│             SchedulerStartupService (IHostedService)             │
│                                                                  │
│  1. Seed all PendingManifests (upsert)                          │
│  2. Collect all configured ExternalIds                          │
│  3. Query DB for manifests NOT in configured set                │
│  4. Delete orphaned manifests + related data                    │
│  5. Clean up orphaned ManifestGroups                            │
└──────────────────────────────────────────────────────────────────┘
```

At startup, after seeding all configured manifests via upsert, the scheduler compares the set of ExternalIds defined in code against all manifests in the database. Any manifest whose ExternalId is not in the configured set is considered **orphaned** and is deleted along with its:

- **WorkQueue** entries (pending dispatches)
- **DeadLetter** records (failed executions)
- **Metadata** records (execution history)

If deleting an orphaned manifest would break a `DependsOnManifestId` foreign key on another manifest, that reference is set to `null` before deletion.

After manifest pruning, any `ManifestGroup` with no remaining manifests is also deleted.

## Configuration

Orphan manifest cleanup is **enabled by default**. No additional configuration is needed — simply remove a schedule definition from your code and restart the application.

### Disabling Cleanup

If you create manifests dynamically at runtime via `IManifestScheduler` (outside of the startup configuration), disable orphan pruning to prevent those manifests from being deleted on restart:

```csharp
services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
        .PruneOrphanedManifests(false)  // Disable orphan cleanup
        .Schedule<IMyTrain>(
            "my-job",
            new MyInput(),
            Every.Minutes(5))
    )
);
```

## Examples

### Removing a Single Schedule

```csharp
// Before: two schedules defined
scheduler
    .Schedule<IHelloWorldTrain>(
        "hello-world",
        new HelloWorldInput { Name = "Scheduler" },
        Every.Seconds(20))
    .Schedule<IGoodbyeWorldTrain>(
        "goodbye-world",
        new GoodbyeWorldInput { Name = "Scheduler" },
        Every.Minutes(1));

// After: "goodbye-world" removed from code
scheduler
    .Schedule<IHelloWorldTrain>(
        "hello-world",
        new HelloWorldInput { Name = "Scheduler" },
        Every.Seconds(20));

// On next startup:
//   - "hello-world" is upserted (no change)
//   - "goodbye-world" is deleted from the database
```

### Removing All Schedules

```csharp
// Before: schedules defined
scheduler
    .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5));

// After: all schedules removed
services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer())
);

// On next startup:
//   - No manifests are seeded
//   - All existing manifests are deleted from the database
```

### Interaction with ScheduleMany PrunePrefix

Orphan manifest cleanup and [ScheduleMany's PrunePrefix]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %}#with-pruning-automatic-stale-cleanup) are complementary:

- **PrunePrefix** operates within a single `ScheduleMany` batch during seeding, removing items that were in a previous deployment but not in the current batch. It runs as part of the seeding transaction.
- **Orphan manifest cleanup** operates globally after all seeding is complete, removing any manifest not in the configured set — including entire `Schedule` definitions that were removed.

Both features compose correctly. PrunePrefix may delete some manifests during seeding, and orphan cleanup catches any remaining orphans afterward.

## Remarks

- Orphan pruning runs once at startup as part of `SchedulerStartupService`, before the polling services begin. It does not run continuously.
- Both single manifests (`.Schedule(...)`) and batch manifests (`.ScheduleMany(...)`) are tracked. The scheduler knows the full set of ExternalIds that each builder call will create, including all items in a batch.
- Deletion follows FK-safe ordering: self-referencing `DependsOnManifestId` is cleared first, then WorkQueue, DeadLetter, and Metadata records, and finally the manifest itself.
- When all schedules are removed from code (empty configuration), all manifests in the database are pruned. This is the expected behavior — the code is the source of truth.
