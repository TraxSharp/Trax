---
title: Resolve a dead letter
description: "How to find a dead-lettered manifest, read why it failed, and requeue or acknowledge it from the dashboard, GraphQL or ITraxScheduler, one at a time or in bulk."
type: how-to
area: scheduling
nav_order: 3
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/TraxScheduler/ITraxScheduler.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/DeadLetterRequeue
---

# Resolve a dead letter

A manifest is dead-lettered when its counted failures exceed `MaxRetries`: with the default of 3,
on its fourth failure inside its failure window. Its dead letter has status
`AwaitingIntervention`, and the ManifestManager skips the manifest until the dead letter is
resolved. Resolving it means one of two things:

- **Requeue**: run the manifest again, once you have fixed what made it fail. The dead letter
  becomes `Retried`.
- **Acknowledge**: record that it was handled and needs no run, with a note. The dead letter
  becomes `Acknowledged`.

Either one resets the manifest's failure count, so its schedule resumes and earlier failures do not
count toward its next dead letter. How a manifest gets here, and what its retries did first, is in
[How retries work](/docs/scheduler/how-retries-work).

## Handling Dead Letters

### 1. Find the dead letter

In the dashboard, open **Data > Dead Letters** (`/trax/data/dead-letters`). Over GraphQL, read
`operations.deadLetters`, which is there once the host calls
[`ExposeOperationQueries()`](/docs/sdk-reference/graphql-api/add-trax-graphql):

```graphql
query {
  operations {
    deadLetters {
      deadLetters(status: AWAITING_INTERVENTION, take: 10) {
        items {
          id
          manifestId
          manifestName
          status
          reason
          retryCountAtDeadLetter
          deadLetteredAt
        }
        totalCount
      }
      deadLetter(id: 42) {
        status
        resolvedAt
        resolutionNote
        retryMetadataId
      }
    }
  }
}
```

The fields and arguments are listed in [Queries: deadLetters](/docs/sdk-reference/graphql-api/queries/dead-letters).

### 2. Read why it failed

The dashboard's dead letter page (the visibility icon on its row) shows the reason, the manifest's
configuration, the latest failure's stack trace and every failed run. From code, read the
manifest's recent failures from the metadata table:

```csharp compile
var failures = await context
    .Metadatas.Where(m => m.ManifestId == manifestId && m.TrainState == TrainState.Failed)
    .OrderByDescending(m => m.StartTime)
    .Take(10)
    .ToListAsync();
```

A dead letter whose cause is outside the train (a downstream system that was down, a bad
deploy since rolled back) can be requeued as it is. One whose input or code is wrong needs the fix
deployed first, or the requeued run fails the same way.

### 3. Requeue or acknowledge it

**From the dashboard**, while the dead letter is `AwaitingIntervention`:

- **Re-queue** queues a run from the manifest's properties. The run
  [replays the failed run's decisions](#retries-replay-decisions) when that is sound.
- **Re-queue, Ask Afresh** does the same, except the run asks its deciders afresh.
- **Acknowledge** asks for a resolution note.

The list page resolves many at once: **Requeue All** and **Acknowledge All** take every
`AwaitingIntervention` dead letter, **Requeue Selected** and **Acknowledge Selected** the ones you
tick, and each requeue has an **Ask Afresh** variant. **Requeue All** starts the background job
[`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs) runs, the
same job the API's `requeueAllDeadLetters` starts, and shows its progress until it ends.

**Over GraphQL**, the mutations are under `operations.deadLetters`, there once the host calls
[`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql):

```graphql
# Requeue a single dead letter
mutation {
  operations {
    deadLetters {
      requeueDeadLetter(id: 42) {
        success
        workQueueId
        message
      }
    }
  }
}

# Acknowledge a single dead letter
mutation {
  operations {
    deadLetters {
      acknowledgeDeadLetter(id: 42, note: "Root cause fixed") {
        success
        message
      }
    }
  }
}

# Batch requeue by IDs
mutation {
  operations {
    deadLetters {
      requeueDeadLetters(ids: [42, 43, 44]) {
        count
        message
      }
    }
  }
}

# Requeue all awaiting dead letters: starts a background job and returns it at once
mutation {
  operations {
    deadLetters {
      requeueAllDeadLetters {
        id
        status
        awaitingAtStart
      }
    }
  }
}

# Read the job until status is no longer RUNNING (on the node that started it)
query {
  operations {
    deadLetters {
      requeueAllJob(id: "6f1c2a7e-0d4b-4f53-9a3e-2b8f1c0d9e11") {
        status
        count
        message
      }
    }
  }
}

# Requeue so the run asks its deciders afresh
mutation {
  operations {
    deadLetters {
      requeueDeadLetter(id: 42, askAfresh: true) {
        success
        message
      }
    }
  }
}
```

`requeueDeadLetter`, `requeueDeadLetters` and `requeueAllDeadLetters` take `askAfresh` (default
`false`). Requeue-all runs as a background job, so a large backlog never outlasts the request: the
request that starts it answers at once with the job, and the requeue finishes even if the client
goes away. A job that stops part-way leaves the dead letters it reached requeued; starting another
requeues the rest. An acknowledgement note is at most 1,000 characters over GraphQL. The fields
are on [Mutations](/docs/sdk-reference/graphql-api/mutations/dead-letters).

**From code**, through `ITraxScheduler`:

```csharp compile
// One dead letter
var requeued = await scheduler.RequeueDeadLetterAsync(deadLetterId);
var acknowledged = await scheduler.AcknowledgeDeadLetterAsync(deadLetterId, "Root cause fixed");

// By id, 1 to 1000 at a time
var requeuedBatch = await scheduler.RequeueDeadLettersAsync([1, 2, 3]);
var acknowledgedBatch = await scheduler.AcknowledgeDeadLettersAsync([4, 5, 6], "Batch fix");

// Every dead letter awaiting intervention
var requeuedAll = await scheduler.RequeueAllDeadLettersAsync();
var acknowledgedAll = await scheduler.AcknowledgeAllDeadLettersAsync("Mass acknowledge");

// Requeue so the run asks its deciders afresh instead of replaying the failed run's decisions
var askedAfresh = await scheduler.RequeueDeadLetterAsync(deadLetterId, askAfresh: true);
```

A single operation returns a `DeadLetterOperationResult` (`Success`, `WorkQueueId`, `Message`), a
batch one a `BatchDeadLetterResult` (`Count`, `Message`). A requeue that cannot be done is not an
exception: it returns `Success = false` with the reason in `Message`. A batch outside 1 to 1000 ids
counts nothing and says why. `RequeueAllDeadLettersAsync` works a page of manifests at a time, so a
large backlog is never loaded at once.

### 4. Confirm the run

A requeued dead letter carries `retryMetadataId` once the JobDispatcher has created the run, which
links the original failure, through the dead letter, to the retry. Read it with
`operations.deadLetters.deadLetter(id:)`, or follow it from the dead letter page in the dashboard.

## What a requeue does

**It resets the failure count.** The ManifestManager counts only failures after the manifest's
latest resolved dead letter (and inside its failure window), so a requeued manifest starts fresh
instead of being dead-lettered again on the failures that put it here.

**It never queues a second entry for a manifest.** A manifest has at most one queued work queue
entry (a unique index enforces it):

- A single requeue whose manifest already has a queued entry fails with a message naming that
  entry, and the dead letter stays `AwaitingIntervention`.
- A batch or "all" requeue skips each dead letter whose manifest already has a queued entry,
  leaving it `AwaitingIntervention`. Dead letters that share a manifest fold into one entry and
  are all marked `Retried`, since each would queue the same work. Each resolution note names the
  entry; the newest dead letter carries its `DeadLetterId`.

`Count` is the number of dead letters resolved, and `Message` says how many were folded and
skipped, for example `"3 dead letter(s) requeued; 1 folded into another dead letter's entry for the same manifest; 2 skipped because their manifest already has a queued entry."`
A skipped dead letter can be requeued once the queued entry has been dispatched.

**It is safe to press twice.** Every operation matches only dead letters still
`awaiting_intervention`, so a second operator resolving the same one gets "not found or already
resolved", and no second entry is queued. The check for a manifest's queued entry and the insert
are separate statements, so a concurrent requeue or the ManifestManager can queue the manifest in
between; the unique index refuses the second entry, a batch requeue rereads and skips that
manifest, and a single requeue reports it as already queued. Two operators pressing **Requeue All**
at once both succeed and leave one queued entry per manifest.

**Acknowledging in bulk is one statement.** On a relational provider, **Acknowledge All** and
**Acknowledge Selected** resolve their dead letters with a single `UPDATE`, so tens of thousands
take a fraction of a second. InMemory loads and saves each row.

### Retries replay decisions

A requeue replays the decisions the manifest's failed run recorded, as an automatic retry does,
when that is sound; `askAfresh` makes the run ask its deciders again instead. When it is sound, and
how to turn it off for a manifest, is in
[How retries work: Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions).

### Retries resume from a checkpoint

For a train whose chain declares a checkpoint, a requeue resumes after the failed run's latest
checkpoint instead of running every step again. See
[How retries work: Retries resume from a checkpoint](/docs/scheduler/how-retries-work#retries-resume-from-a-checkpoint).

### When a retry runs

A requeued run is dispatched once its entry is claimed, with no backoff. An automatic retry waits
for the manifest's next due run and a backoff; see
[How retries work: When a retry runs](/docs/scheduler/how-retries-work#when-a-retry-runs).

## Dead Letter Auto-Purge

Resolved dead letters (`Retried` or `Acknowledged`) are deleted once `ResolvedAt` is older than
`DeadLetterRetentionPeriod` (30 days by default), by the `DeadLetterCleanupTrain` every hour. It is
on by default. A dead letter `AwaitingIntervention` is never deleted, and neither is a retried one
whose requeued entry is still `Queued` (a paused dispatcher, or a group at its limit), since that
would delete the retry before it ran; it goes on a later run.

To keep them longer, set the retention; `AutoPurgeDeadLetters(false)` keeps them forever:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.DeadLetterRetentionPeriod(TimeSpan.FromDays(90))
        )
);
```

Both can also be changed at runtime, from the dashboard's Server Settings page or the
[`updateScheduler`](/docs/sdk-reference/graphql-api/mutations/config) mutation.
The purge reads them on every run, so turning it off takes effect from the next run without a
restart. Both delete data, so when the code states one and a saved setting differs, the scheduler
runs with whichever keeps more:

| Code | Saved | Runs with |
|------|-------|-----------|
| `AutoPurgeDeadLetters(false)` | `true` | No purge |
| `AutoPurgeDeadLetters(true)` or unstated | `false` | No purge |
| `DeadLetterRetentionPeriod(90 days)` | 7 days | 90 days |
| `DeadLetterRetentionPeriod(7 days)` | 90 days | 90 days |

When the code does not state a setting, a saved value replaces the default. Whenever the saved
value differs from the code's, the scheduler logs a warning naming the setting, both values and
the one it runs with.

## Testing dead letters

`UseInMemory()` runs jobs inline and has no work queue, so it neither delays retries nor replays
decisions as Postgres does. To test retries, dead letters and requeues as they run in production,
start the real host against a test Postgres and wait on the `trax.metadata` and
`trax.dead_letter` rows, as the [Scheduling sample's E2E suite](/docs/samples/scheduling#tests)
does.

## SDK Reference

> [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler) | [IDeadLetterRequeueJobs](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Mutations](/docs/sdk-reference/graphql-api/mutations)
