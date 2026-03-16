---
layout: default
title: Dead Letters & Cleanup
parent: Scheduling
nav_order: 3
---

# Dead Letters, Monitoring & Cleanup

## Handling Dead Letters

When a job exceeds `MaxRetries`, it enters the dead letter queue with status `AwaitingIntervention`. The ManifestManager will skip these manifests until they're resolved.

To resolve a dead letter, use either the **Dashboard UI** or code:

### Via Dashboard

Navigate to **Data > Dead Letters** and click the visibility icon on any row. The dead letter detail page shows full context — the dead letter reason, manifest configuration, the most recent failure's stack trace, and a history of all failed runs.

Two actions are available while the dead letter is in `AwaitingIntervention` status:

- **Re-queue** — Creates a new WorkQueue entry from the manifest's properties and marks the dead letter as `Retried`
- **Acknowledge** — Prompts for a resolution note and marks the dead letter as `Acknowledged`

### Via Code

```csharp
// Option 1: Retry (creates a new execution)
deadLetter.Status = DeadLetterStatus.Retried;
deadLetter.ResolvedAt = DateTime.UtcNow;
deadLetter.ResolutionNote = "Root cause fixed, retrying";
// Then create a new Metadata and enqueue it

// Option 2: Acknowledge (mark as handled, no retry)
deadLetter.Status = DeadLetterStatus.Acknowledged;
deadLetter.ResolvedAt = DateTime.UtcNow;
deadLetter.ResolutionNote = "Data was manually corrected";

await context.SaveChanges(ct);
```

## Monitoring

The **Trax.Core Dashboard** at `/trax/data/dead-letters` provides a real-time view of all dead letters with status badges and links to detail pages. The dead letter detail page surfaces the full failure context — stack traces, inputs, and execution history — so operators can make informed retry/acknowledge decisions without writing queries.

The built-in local workers execute JobRunner jobs using PostgreSQL's `background_job` table. Worker health and job status can be monitored via the Trax Dashboard at `/trax`.

For train-level details, query the `Metadata` table:

```csharp
// Recent failures for a manifest
var failures = await context.Metadatas
    .Where(m => m.ManifestId == manifestId && m.TrainState == TrainState.Failed)
    .OrderByDescending(m => m.StartTime)
    .Take(10)
    .ToListAsync();
```

## Metadata Cleanup

System trains like `ManifestManagerTrain` run frequently (every 5 seconds by default), generating metadata rows that have no long-term value. The metadata cleanup service automatically purges old entries to keep the database clean.

### How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│        MetadataCleanupPollingService (BackgroundService)         │
│            Polls on CleanupInterval (default: 1 minute)         │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  MetadataCleanupTrain                         │
│                                                                  │
│  DeleteExpiredMetadataJunction:                                      │
│    1. Find metadata matching whitelist + older than retention    │
│    2. Only terminal states (Completed / Failed / Cancelled)      │
│    3. Delete associated work queue entries (FK safety)            │
│    4. Delete associated log entries (FK safety)                  │
│    5. Delete metadata rows                                       │
└─────────────────────────────────────────────────────────────────┘
```

The cleanup only targets metadata in **terminal states** (Completed, Failed, or Cancelled). Pending and InProgress metadata is never deleted, regardless of age. Associated work queue entries and log entries are deleted first to avoid foreign key constraint violations.

Deletion uses EF Core's `ExecuteDeleteAsync` for efficient single-statement SQL—no entities are loaded into memory.

### Enabling Cleanup

Add `.AddMetadataCleanup()` to your scheduler configuration. By default this cleans up `ManifestManagerTrain`, `JobDispatcherTrain`, and `MetadataCleanupTrain` metadata older than 1 hour, checking every minute.

*SDK Reference: [AddMetadataCleanup]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-metadata-cleanup.md %}) — all configuration options including `RetentionPeriod`, `CleanupInterval`, and adding custom train types to the whitelist.*

See [MetadataCleanup](admin-trains/metadata-cleanup.md) for details on how the cleanup train operates internally.

### What Gets Deleted

A metadata row is deleted when **all** of these conditions are true:

1. Its `Name` matches a train in the whitelist
2. Its `StartTime` is older than the retention period
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

Any work queue entries and log entries associated with deleted metadata are also removed.

{: .note }
Cancelled trains are treated as terminal — they are eligible for cleanup but are **not retried** and **do not create dead letters**. Cancellation is an explicit operator action, not a transient failure.

## Testing

For integration tests, use `UseInMemory()` and the scheduler will automatically use `InMemoryJobSubmitter`:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UseInMemory())
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);
```

Jobs execute inline, so tests are fast and don't need database infrastructure.
