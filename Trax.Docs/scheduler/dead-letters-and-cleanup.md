---
layout: default
title: Dead Letters & Cleanup
parent: Scheduling
nav_order: 3
---

# Dead Letters, Monitoring & Cleanup

## Handling Dead Letters

When a job exceeds `MaxRetries`, it enters the dead letter queue with status `AwaitingIntervention`. The ManifestManager will skip these manifests until they're resolved.

To resolve a dead letter, use the **Dashboard UI**, the **GraphQL API**, or the **ITraxScheduler** service directly.

### Via Dashboard

Navigate to **Data > Dead Letters** and click the visibility icon on any row. The dead letter detail page shows full context, the dead letter reason, manifest configuration, the most recent failure's stack trace, and a history of all failed runs.

Two actions are available while the dead letter is in `AwaitingIntervention` status:

- **Re-queue**: Creates a new WorkQueue entry from the manifest's properties and marks the dead letter as `Retried`
- **Acknowledge**: Prompts for a resolution note and marks the dead letter as `Acknowledged`

#### Batch Operations

The dead letters list page supports batch operations for resolving multiple dead letters at once:

- **Requeue All / Acknowledge All**: Resolves every `AwaitingIntervention` dead letter in a single operation
- **Requeue Selected / Acknowledge Selected**: Use the checkboxes to select specific dead letters, then resolve just those

### Via GraphQL

Dead letter mutations are available under the `deadLetters` namespace:

```graphql
# Requeue a single dead letter
mutation {
  deadLetters {
    requeueDeadLetter(id: 42) {
      success
      workQueueId
      message
    }
  }
}

# Acknowledge a single dead letter
mutation {
  deadLetters {
    acknowledgeDeadLetter(id: 42, note: "Root cause fixed") {
      success
      message
    }
  }
}

# Batch requeue by IDs
mutation {
  deadLetters {
    requeueDeadLetters(ids: [42, 43, 44]) {
      count
      message
    }
  }
}

# Requeue all awaiting dead letters
mutation {
  deadLetters {
    requeueAllDeadLetters {
      count
      message
    }
  }
}
```

Query dead letters with optional status filtering:

```graphql
query {
  deadLetters {
    getDeadLetters(status: AWAITING_INTERVENTION, take: 10) {
      items {
        id
        manifestName
        status
        reason
        deadLetteredAt
      }
      totalCount
    }
  }
}
```

### Via ITraxScheduler

All dead letter operations are available programmatically through `ITraxScheduler`:

```csharp
// Single operations
var result = await scheduler.RequeueDeadLetterAsync(deadLetterId);
var result = await scheduler.AcknowledgeDeadLetterAsync(deadLetterId, "Root cause fixed");

// Batch by IDs
var result = await scheduler.RequeueDeadLettersAsync(new long[] { 1, 2, 3 });
var result = await scheduler.AcknowledgeDeadLettersAsync(new long[] { 1, 2, 3 }, "Batch fix");

// Resolve all
var result = await scheduler.RequeueAllDeadLettersAsync();
var result = await scheduler.AcknowledgeAllDeadLettersAsync("Mass acknowledge");
```

### Failure Counter Reset

Resolving a dead letter (either action) resets the manifest's failure counter. The ManifestManager only counts failures that occurred **after** the most recent resolution when comparing against `MaxRetries`. This means a retried manifest starts fresh, it won't be immediately re-dead-lettered based on the same failures that triggered the original dead letter.

### RetryMetadataId Linking

When a dead letter is requeued, the new WorkQueue entry carries a `DeadLetterId` reference. When the JobDispatcher creates a Metadata record for that entry, it automatically links the `RetryMetadataId` back on the dead letter. This creates a traceable chain from the original failure through the dead letter to the retry execution.

### Concurrency Safety

All dead letter operations filter by `status = 'awaiting_intervention'` at query time. If two users resolve the same dead letter simultaneously, the second operation sees no matching record and returns a "not found or already resolved" result. No duplicate work queue entries are created.

## Retry Delay & Backoff

When a manifest has failures but hasn't reached `MaxRetries`, the scheduler applies an exponential backoff delay before retrying:

```
delay = min(DefaultRetryDelay * RetryBackoffMultiplier ^ (failureCount - 1), MaxRetryDelay)
```

With defaults (`DefaultRetryDelay: 5m`, `RetryBackoffMultiplier: 2.0`, `MaxRetryDelay: 1h`):

| Failure # | Delay |
|-----------|-------|
| 1 | 5 minutes |
| 2 | 10 minutes |
| 3 | 20 minutes |
| 4 | 40 minutes |
| 5+ | 1 hour (capped) |

The delay is implemented by setting `ScheduledAt` on the WorkQueue entry. The JobDispatcher skips entries where `ScheduledAt > now`, so the retry won't be dispatched until the delay has elapsed.

Configure via the scheduler builder:

```csharp
.AddScheduler(scheduler => scheduler
    .DefaultRetryDelay(TimeSpan.FromMinutes(5))
    .RetryBackoffMultiplier(2.0)
    .MaxRetryDelay(TimeSpan.FromHours(1))
)
```

## Monitoring

The **Trax.Core Dashboard** at `/trax/data/dead-letters` provides a real-time view of all dead letters with status badges and links to detail pages. The dead letter detail page surfaces the full failure context, stack traces, inputs, and execution history, so operators can make informed retry/acknowledge decisions without writing queries.

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

Deletion uses EF Core's `ExecuteDeleteAsync` for efficient single-statement SQL. No entities are loaded into memory.

### Enabling Cleanup

Add `.AddMetadataCleanup()` to your scheduler configuration. By default this cleans up `ManifestManagerTrain` and `MetadataCleanupTrain` metadata older than 30 minutes, checking every minute.

See [MetadataCleanup](admin-trains/metadata-cleanup.md) for details on how the cleanup train operates internally.

### What Gets Deleted

A metadata row is deleted when **all** of these conditions are true:

1. Its `Name` matches a train in the whitelist
2. Its `StartTime` is older than the retention period
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

Any work queue entries and log entries associated with deleted metadata are also removed.

Cancelled trains are treated as terminal, they are eligible for cleanup but are **not retried** and **do not create dead letters**. Cancellation is an explicit operator action, not a transient failure.

## Dead Letter Auto-Purge

Resolved dead letters (Retried or Acknowledged) are automatically purged after the configured retention period. This is enabled by default.

The `DeadLetterCleanupTrain` runs on its own polling interval (default: 1 hour) and deletes resolved dead letters where `ResolvedAt` is older than `DeadLetterRetentionPeriod` (default: 30 days). Dead letters in `AwaitingIntervention` status are never deleted.

Configure via the scheduler builder:

```csharp
.AddScheduler(scheduler => scheduler
    // Disable auto-purge (dead letters accumulate forever)
    .AutoPurgeDeadLetters(false)

    // Or customize retention
    .DeadLetterRetentionPeriod(TimeSpan.FromDays(90))
)
```

Runtime settings can be adjusted via the Dashboard at `/trax/data/settings`.

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

## SDK Reference

> [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [ManifestManagement](/docs/sdk-reference/scheduler-api/manifest-management)
