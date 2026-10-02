---
layout: default
title: Orphan Manifest Cleanup
description: How the scheduler deletes manifests at startup whose schedule was removed from code, which manifests a host owns, and how to disable or scope the cleanup.
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
│  3. Query DB for this app's manifests NOT in configured set     │
│  4. Delete orphaned manifests + related data                    │
│  5. Clean up orphaned ManifestGroups                            │
└──────────────────────────────────────────────────────────────────┘
```

At startup, after seeding all configured manifests via upsert, the scheduler compares the set of ExternalIds defined in code against the manifests in the database that this application owns. Any of those whose ExternalId is not in the configured set is considered **orphaned** and is deleted along with its:

- **WorkQueue** entries (pending dispatches)
- **DeadLetter** records (failed executions)
- **Metadata** records of finished runs (execution history)

What the prune leaves alone:

| Case | What happens |
|---|---|
| A manifest another application owns, or one with no owner | Never deleted by this host's prune (see [Ownership](#ownership)). |
| The host's application name cannot be found | Nothing is pruned, and a warning says why. |
| The host declares no manifests | Nothing is pruned, and the log says why. An API or worker host that calls `AddScheduler` only to reach `ITraxScheduler` or the operations service has no basis for calling another host's manifests orphaned. |
| An orphan has a `Pending` or `InProgress` run | The manifest and the run are kept. The next start prunes the manifest once the run has finished. A prune never deletes an unfinished run. |
| A run of the orphan started a nested train | The nested run is kept, with its `ParentId` cleared, as [metadata cleanup](/docs/scheduler/dead-letters-and-cleanup#metadata-cleanup) does. |
| Another manifest depends on the orphan | Its `DependsOnManifestId` is set to `null`. |

Orphan pruning deletes manifests in batches (500 per batch) to keep SQL `IN(...)` clauses small and avoid command timeouts on large prune operations. Each batch runs in one transaction: it clears the references above, then deletes WorkQueues, DeadLetters, Metadata, and finally the manifests. A batch that fails rolls back, is logged, and the prune moves on to the next one. A failure of the prune as a whole is logged too, and the host starts anyway: pruning is housekeeping.

After manifest pruning, any `ManifestGroup` with no remaining manifests is also deleted.

### Ownership

Every manifest the scheduler writes, whether seeded from the builder or created through `ITraxScheduler` at runtime, records the application that wrote it in `Manifest.Owner`: the host environment's `IHostEnvironment.ApplicationName`, or the entry assembly's name when no `IHostEnvironment` is registered. The prune considers only manifests whose owner is this application's name, so several applications can schedule against one database without deleting each other's manifests. Hosts of one application (instances behind a load balancer, say) share the name, so they should declare the same schedules.

A manifest with no owner is never deleted by the prune. Every manifest written by an earlier Trax version starts out that way, and gets its owner the next time the application that declares it seeds it. One the application no longer declares is therefore never seeded again and never pruned: delete such manifests by hand, from the dashboard or the database. When no application name can be found, the prune deletes nothing and logs a warning.

## Configuration

Orphan manifest cleanup is **enabled by default**. No additional configuration is needed, simply remove a schedule definition from your code and restart the application.

### Disabling Cleanup

If you create manifests dynamically at runtime via `ITraxScheduler` (outside of the startup configuration), disable orphan pruning to prevent those manifests from being deleted on restart:

```csharp
services.AddTrax(trax => trax
    .AddScheduler(scheduler => scheduler
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
services.AddTrax(trax => trax
    .AddScheduler()
);

// On next startup:
//   - No manifests are seeded
//   - Nothing is pruned: a host that declares no manifests leaves the table alone
```

To remove every manifest, delete them from the dashboard or the database. The scheduler will not treat "this host declares nothing" as "delete everything".

### Interaction with ScheduleMany PrunePrefix

Orphan manifest cleanup and [ScheduleMany's PrunePrefix](/docs/sdk-reference/scheduler-api/schedule-many#with-pruning-automatic-stale-cleanup) are complementary:

- **PrunePrefix** operates within a single `ScheduleMany` batch during seeding, removing items that were in a previous deployment but not in the current batch. It matches by external ID prefix (and, for a named batch, group) only; it does not check a manifest's owner. It runs in a separate database context after the main seeding transaction commits, so a prune failure does not roll back the upserted manifests. A named batch prunes only within its own group. It follows the same rules as the orphan prune for unfinished runs, nested runs and transactions.
- **Orphan manifest cleanup** operates after all seeding is complete, removing any of this application's manifests not in the configured set, including entire `Schedule` definitions that were removed.

Both features compose correctly. PrunePrefix may delete some manifests during seeding, and orphan cleanup catches any remaining orphans afterward.

## Remarks

- Orphan pruning runs once at startup as part of `SchedulerStartupService`, before the polling services begin. It does not run continuously.
- Both single manifests (`.Schedule(...)`) and batch manifests (`.ScheduleMany(...)`) are tracked. The scheduler knows the full set of ExternalIds that each builder call will create, including all items in a batch.
- Deletion follows FK-safe ordering: self-referencing `DependsOnManifestId` is cleared first, then WorkQueue, DeadLetter, and Metadata records, and finally the manifest itself.
- When a host declares no schedules (empty configuration), or its application name cannot be found, nothing is pruned.

## SDK Reference

> [PruneOrphanedManifests](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule)
