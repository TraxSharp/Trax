---
layout: default
title: Dead Letters & Cleanup
description: How a manifest dead-letters after MaxRetries within its failure window, and how to requeue or acknowledge dead letters, configure backoff and clean up metadata.
parent: Scheduling
nav_order: 3
---

# Dead Letters, Monitoring & Cleanup

## Handling Dead Letters

When a job's counted failures exceed `MaxRetries` (the retries allowed after the first run, so the default of 3 dead-letters on the fourth failure), it enters the dead letter queue with status `AwaitingIntervention`. Failures count only while they started within the manifest's failure window (`FailureCountWindow`, default 24 hours, unless the manifest states its own `FailureWindow`), so failures spread over weeks do not dead-letter a manifest. The ManifestManager will skip these manifests until they're resolved.

To resolve a dead letter, use the **Dashboard UI**, the **GraphQL API**, or the **ITraxScheduler** service directly.

### Via Dashboard

Navigate to **Data > Dead Letters** and click the visibility icon on any row. The dead letter detail page shows full context, the dead letter reason, manifest configuration, the most recent failure's stack trace, and a history of all failed runs.

Two actions are available while the dead letter is in `AwaitingIntervention` status:

- **Re-queue**: Creates a new WorkQueue entry from the manifest's properties and marks the dead letter as `Retried`. The run [replays the failed run's decisions](#retries-replay-decisions) when that is sound.
- **Re-queue, Ask Afresh**: The same, except the run asks its deciders afresh
- **Acknowledge**: Prompts for a resolution note and marks the dead letter as `Acknowledged`

#### Batch Operations

The dead letters list page supports batch operations for resolving multiple dead letters at once:

- **Requeue All / Acknowledge All**: Resolves every `AwaitingIntervention` dead letter in a single operation. Requeue All starts the background job [`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs) runs, the same job the API's `requeueAllDeadLetters` starts, and shows its progress until it ends
- **Requeue Selected / Acknowledge Selected**: Use the checkboxes to select specific dead letters, then resolve just those
- **Requeue All, Ask Afresh / Requeue Selected, Ask Afresh**: Requeue as above, with each run asking its deciders afresh

### Via GraphQL

Dead letter operations live under `operations.deadLetters`. The `operations` namespace is opt-in (off by default), wired via [`ExposeOperationQueries()`](/docs/sdk-reference/graphql-api/add-trax-graphql) and [`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql) on the `AddTraxGraphQL` builder. Once enabled:

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
`false`), as `triggerManifest` and `triggerManifestDelayed` do; see
[Retries replay decisions](#retries-replay-decisions).

`requeueDeadLetter` and `acknowledgeDeadLetter` return `DeadLetterOperationResult`
(`success: Boolean!`, `workQueueId: Long`, `message: String!`); `workQueueId` is the entry a
requeue queued and `null` otherwise. The batch variants return `BatchDeadLetterResult`
(`count: Int!`, `message: String!`). A requeue that cannot be done (the dead letter is already
resolved, or its manifest already has a queued entry) is not a GraphQL error: it returns
`success: false` with the reason in `message`.

Over GraphQL, requeue-all runs as a background job so a large backlog never outlasts the request:
the request that starts it answers at once with the job, and the requeue finishes even if the
client goes away. A job that stops part-way (a server failure, a shutdown) leaves the dead letters
it reached requeued; starting another requeues the rest. The fields are on
[Mutations](/docs/sdk-reference/graphql-api/mutations#deadletters-nested-namespace). An
acknowledgement note is at most 1,000 characters over GraphQL; a longer one is refused.

Read dead letters with `operations.deadLetters.deadLetters` (a page, optionally filtered by status)
and `operations.deadLetters.deadLetter(id:)`:

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

The fields and arguments are listed in [Queries: deadLetters](/docs/sdk-reference/graphql-api/queries#deadletters-nested-under-operations).

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

// Requeue so the run asks its deciders afresh instead of replaying the failed run's decisions
var result = await scheduler.RequeueDeadLetterAsync(deadLetterId, askAfresh: true);
var result = await scheduler.RequeueDeadLettersAsync(new long[] { 1, 2, 3 }, askAfresh: true);
var result = await scheduler.RequeueAllDeadLettersAsync(askAfresh: true);
```

A requeue [replays the decisions](#retries-replay-decisions) of the manifest's failed run when that
is sound, as an automatic retry does. Pass `askAfresh: true` to queue the run to ask its deciders
again instead.

The batch methods take 1 to `OperationsService.MaxBatchSize` (1000) ids, the limit every operations-surface batch has. An empty list or a longer one is refused: the result counts nothing and its message says why. `RequeueAllDeadLettersAsync` reads and requeues a page of manifests at a time and sums the counts, so a large backlog is never loaded at once; every dead letter of one manifest is in the same page, so they still fold into one entry.

### Failure Counter Reset

Resolving a dead letter (either action) resets the manifest's failure counter. The ManifestManager only counts failures that occurred **after** the most recent resolution (and inside `FailureCountWindow`) when comparing against `MaxRetries`. This means a retried manifest starts fresh, it won't be immediately re-dead-lettered based on the same failures that triggered the original dead letter.

### One Queued Entry per Manifest

A manifest can have only one queued work queue entry at a time (a unique index enforces it), so a requeue never creates a second one:

- **A single requeue** whose manifest already has a queued entry fails with a message naming that entry, and the dead letter stays `AwaitingIntervention`.
- **A batch or "all" requeue** skips each dead letter whose manifest already has a queued entry, leaving it `AwaitingIntervention`. Dead letters that share a manifest are folded into one entry and all marked `Retried`, since a requeue runs the manifest's own properties and each would queue the same work. Each resolution note names the entry; the newest dead letter carries the entry's `DeadLetterId`.

`count` is the number of dead letters resolved, and `message` says how many were folded and how many were skipped, for example `"3 dead letter(s) requeued; 1 folded into another dead letter's entry for the same manifest; 2 skipped because their manifest already has a queued entry."` A skipped dead letter can be requeued once the queued entry has been dispatched.

### Acknowledging in Bulk

On a relational provider, **Acknowledge All** and **Acknowledge Selected** resolve their dead letters with a single `UPDATE`, so acknowledging a backlog of tens of thousands takes a fraction of a second. The InMemory provider has no set-based update, so there each row is loaded and saved.

### RetryMetadataId Linking

When a dead letter is requeued, the new WorkQueue entry carries a `DeadLetterId` reference. When the JobDispatcher creates a Metadata record for that entry, it automatically links the `RetryMetadataId` back on the dead letter. This creates a traceable chain from the original failure through the dead letter to the retry execution.

### Concurrency Safety

All dead letter operations filter by `status = 'awaiting_intervention'` at query time. If two users resolve the same dead letter simultaneously, the second operation sees no matching record and returns a "not found or already resolved" result. No duplicate work queue entries are created. The check for a manifest's existing queued entry and the insert are separate statements, so a concurrent requeue or the ManifestManager can queue that manifest in between. The unique index refuses the second entry; a batch or "all" requeue then rereads and retries, skipping that manifest, and a single requeue reports it as already queued. Two operators pressing **Requeue All** at once both succeed and leave one queued entry per manifest.

## Retry Delay & Backoff

When a manifest's latest finished run failed and its counted failures have not exceeded `MaxRetries`, the scheduler delays the next run, the retry, by an exponential backoff. `failureCount` is the number of failures inside the manifest's failure window (its `FailureWindow`, or the scheduler's `FailureCountWindow`) since the latest resolved dead letter, so a failure older than the window no longer lengthens the delay:

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

Only a retry waits. Once a run succeeds or is cancelled, the next occurrence runs on time, however many failures are still inside the window; those failures still set the length of the next retry's delay and still count toward the dead letter. A requeued dispatch attempt is not a finished run and does not count either way.

The delay is implemented by setting `ScheduledAt` on the WorkQueue entry. The JobDispatcher skips entries where `ScheduledAt > now()`, so the retry won't be dispatched until the delay has elapsed. That comparison runs on the database's clock, so `ScheduledAt` is the database's time plus the delay, not the scheduler host's. A retry whose delay is zero (`DefaultRetryDelay(TimeSpan.Zero)`) stores no `ScheduledAt` at all and is dispatched on the next poll, however far the host's clock runs ahead of the database's.

### When a retry runs

A retry is not a timer of its own. It is the manifest's next due run, queued with that delay. A
failed run does not move the point a schedule counts from (an interval counts from the last
success, a cron from its last success's occurrence), so a manifest that failed while it was due is
still due, and the ManifestManager queues the retry on its next cycle, `delay` from then:

| The failed run was | Its retry is queued |
|---|---|
| A due interval or cron run | On the next ManifestManager cycle, dispatched once the delay passes |
| A dependent's run | On the next cycle: the parent's success it was started for has still not been consumed |
| A one-off's run | On the next cycle, while the one-off has not succeeded |
| Started off-schedule by a trigger, with the manifest not due | Only once the manifest is next due by its schedule; then after the delay |

So with `MaxRetries(2)`, `DefaultRetryDelay(2 s)` and the default multiplier, a due manifest that
keeps failing runs three times within about ten seconds and is dead-lettered on the next cycle.
The [Scheduling sample](/docs/samples/scheduling) runs exactly that.

Configure via the scheduler builder:

```csharp
.AddScheduler(scheduler => scheduler
    .DefaultRetryDelay(TimeSpan.FromMinutes(5))
    .RetryBackoffMultiplier(2.0)
    .MaxRetryDelay(TimeSpan.FromHours(1))
)
```

## Retries replay decisions

A retry exists because something after a decision failed: a tool step threw, a database timed out.
Trax has no per-junction retry, so a retry runs the chain from its first junction, and asking a
model again costs a call per question and can be answered differently. Only a manifest's run is retried this
way: a train run directly, queued through the operations `queueTrain` mutation, or queued by a
`[TraxMutation]` dispatch mutation with `mode: QUEUE` (or `GraphQLOperation.Queue`) has no retry
and never replays, and never becomes a dead letter, so a train
meant to recover runs from a manifest (`Schedule`, or `ScheduleOnceAsync` for a single run).
[Building a train that recovers](/docs/effect/decisions#building-a-train-that-recovers) lists every
piece it needs. So when a manifest's run
fails, its retry, queued by the ManifestManager, and a requeue of its dead letter replay the
[decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions) the failed
run recorded: the retry takes the tracks the failed run's deciders chose instead of asking them
again. Only the answers are replayed. Every ordinary junction runs again, side effects included.

The scheduler reads the run to replay from the database, never from a caller: it is the manifest's
latest finished run, and only when the next run is a retry of it, that is, the run failed and its
failure still counts toward the retries (the same test that applies the backoff). A failure that an
acknowledged dead letter or the failure window has set aside is not retried, so the next
occurrence asks afresh. It links the retry only when all of these hold:

| Check | Otherwise |
|---|---|
| The manifest replays decisions on retry (`ReplayDecisionsOnRetry`, on by default) | Asked afresh |
| The failed run asked its deciders itself, rather than replaying another run's answers | Asked afresh |
| Nothing else replays the failed run: no run in any state (queued, running or finished) and no queued entry | Asked afresh |
| For a dependent manifest, its parent has not succeeded again since the failed run was dispatched; a new parent success fires a new run, not a retry | Asked afresh |
| The failed run is a run of the manifest's train, recorded its decisions and acted on at least one | Asked afresh |
| The failed run was queued by this manifest, with no subject key, and with exactly the input and input type the retry is queued with | Asked afresh |

So a retry replays at most once in a row: answers that were replayed into a failure are not
replayed again, and the next retry asks afresh. A run's answers are replayed at most once, and that
is enforced when an entry is dispatched: an entry naming a run that another run already replays is
dispatched with its link dropped, so it runs asking afresh, and the dispatcher logs it at
`Information`. A manifest edited between the failure and the retry
asks afresh, because the answers were given about the old input. Asking afresh is never an error,
and a lookup that fails is logged and asks afresh rather than holding up the retry. A dependent's
failed run is dated by when its work queue entry was dispatched (`work_queue.dispatched_at`), which
the database stamps as it stamps the parent's last success; a run with no dispatched entry falls
back to its start time.

A replay that never started still counts as the one replay. A retry queued to replay that never
ran (its dispatch ran out of attempts, its stored input could not be read, the stale pending reaper
failed it) keeps its link, so as the manifest's latest failed run it makes the next retry ask
afresh. Those answers are not offered to a later retry: a replay can be lost, but never applied
twice. A requeue of such a run follows the kept link to the answers it never replayed.

A run whose replay could not be honoured, and which asked its deciders afresh instead (its metadata
marked `replay_abandoned`), is different: its answers are its own. It counts as a run that asked
its deciders itself, so the next retry may replay its answers once, and it does not use up the
replay of the run it names.

The "nothing else replays the failed run" check reads the run history, so
[metadata cleanup](#metadata-cleanup) keeps that history whole on every host: it never deletes a
run that replays another while it keeps the run replayed. Both expired, they are deleted in the
same transaction; a replay whose source is kept stays with it.

A link can still become impossible to honour after it is written: the failed run can be deleted
before the retry runs, or the retry can land on a host that does not record decisions. A manifest's
run then logs a warning and asks afresh, rather than failing as a manual requeue would.

Within the replay, each answer is still checked one by one: it replays only into a question asked
the same way, about a state that hashes the same, and only while it is younger than
[`ReplayAnswersFor`](/docs/sdk-reference/configuration/add-decision-recording#decisionrecordingoptions)
(24 hours by default). An answer that fails any of these is asked afresh. The InMemory provider
does not replay retries, because its ManifestManager dispatches without work queue entries to
compare inputs against. Replay needs [`AddDecisionRecording()`](/docs/sdk-reference/configuration/add-decision-recording)
on the hosts that run the manifest's train.

### Asking afresh on purpose

A manifest whose questions should always be answered on current information turns replay off:

```csharp
scheduler.Schedule<IScoreLeadsTrain>(
    "score-leads",
    new ScoreLeadsInput(),
    Every.Minutes(30),
    options => options.ReplayDecisionsOnRetry(false));
```

The flag is stored on the manifest as `replay_decisions_on_retry`, so every scheduler host reads the
same value. Stated in code, it is written on every seed, so `ReplayDecisionsOnRetry(true)` in code
turns replay back on at each restart over an operator's runtime opt-out, as every stated setting
does. Where operators manage the flag at runtime, leave it out of code. Left unstated, a new
manifest replays and an existing one keeps its value. [`IOperationsService.SetManifestsReplayDecisionsOnRetryAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#batch-actions)
sets it at runtime. Turning it off reaches a retry already queued: the write clears the link on the
manifest's queued entry in the same transaction as the flag, its message counts the retries it
cleared (`"2 queued retry(s) no longer replay a failed run's decisions."`), and the dispatcher
checks the flag again when it claims an entry. An
opt-out committed in the instant between that check and the run's start still lets that one run
replay.

For a single occasion, the dead-letter requeues, `TriggerAsync` and
[`RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync)
take `askAfresh`. A requeue asked afresh queues no link, and a trigger asked afresh clears the link
of the queued retry it releases. The trigger clears it only while the entry is still queued: when
the dispatcher claimed the entry first, the run replays anyway, and the trigger says so. Its
[`ManifestTriggerResult`](/docs/sdk-reference/scheduler-api/manifest-management#manifesttriggerresult)
has `AlreadyDispatched` set and `ReplayDecisionsOf` naming the run whose decisions it replays, and
a warning is logged.

A requeue of an execution keeps the replay-once rule too. When a queued entry or a run, in any
state, already replays the run being re-queued, `RequeueExecutionAsync` still queues it but asks
afresh, and its message ends `"It asks its deciders afresh: the decisions of execution 42 are
already replayed by another run or queued entry, and are replayed once."` A manifest's retry does
not link a run a requeue replays, and a requeue does not link a run a retry replays. The database
holds one queued entry per replayed run (the unique index `ix_work_queue_unique_queued_replay`), so
when two hosts, two operators, or a requeue and a retry queue a replay of the same run in the same
instant, the one that loses is queued again without the link and asks afresh. That holds for a
retry, a dead-letter requeue, a requeue-all and `RequeueExecutionAsync`; nothing fails, and a
requeue's message says it asked afresh.
`Trax.Scheduler/docs/adr/0017` records why retries replay, and why only once.

## Retries resume from a checkpoint

A train whose chain declares a checkpoint (`Checkpoint<TState>()`) stores that state when a run reaches it. A manifest's retry and a requeue of its dead
letter then resume after the failed run's latest checkpoint instead of running every step again:
the new entry names the failed run in `ResumeFrom`, and the run skips every step before the
checkpoint, including the writes it already committed. Declaring the checkpoint is the opt-in; it
does not depend on `ReplayDecisionsOnRetry`, which still decides whether the retry replays the
failed run's decisions.

The failed run is found as the replay finds it: the manifest's latest finished run, failed, a run of
the manifest's train dispatched from an entry of the manifest with no subject key and exactly the
input the retry is given; a dependent whose parent has succeeded again since is a new firing. The
scheduler then asks whether the train's declared chain can resume after that run's latest
checkpoint, following a run that was itself a resume back to the checkpoints it resumed from, so a
second failure after a resume resumes from the same checkpoint. When the run stored no checkpoint,
when a checkpoint no longer matches the chain or the state's shape after a deploy, or when the
lookup fails, the retry reruns the chain from the top as before. A run a state machine invoked is
never resumed this way.

A run has at most one queued resume (the unique index `ix_work_queue_unique_queued_resume`). When an
operator's [`resumeExecution`](/docs/sdk-reference/scheduler-api/i-operations-service#resumeexecutionasync)
and a retry of the same run are queued in the same instant, one of them carries the resume: a retry
that loses is queued without it and reruns from the top, and an operator's resume that loses is
refused. `requeueExecution` always reruns from the top.

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

Deletion is set-based (`ExecuteDeleteAsync`), in batches of up to `DeleteBatchSize` runs, each batch one transaction. No entities are loaded into memory.

### Enabling Cleanup

Add `.AddMetadataCleanup()` to your scheduler configuration. By default this cleans up the internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) metadata older than 30 minutes, checking every minute.

See [MetadataCleanup](/docs/scheduler/admin-trains/metadata-cleanup) for details on how the cleanup train operates internally.

### What Gets Deleted

A metadata row is deleted when **all** of these conditions are true:

1. Its `Name` matches a train in the whitelist
2. Its `StartTime` is older than the retention period
3. Its `TrainState` is `Completed`, `Failed`, or `Cancelled`

Any work queue entries and log entries associated with deleted metadata are also removed.

Decision replay adds three conditions. A run is kept while a queued work queue entry, or a run that
is itself kept, names it in `replay_decisions_of`, because something still replays it. A run that
replays another is never deleted while the run it replays is kept: both expired, they are deleted
together in one transaction, so a batch can hold more rows than `DeleteBatchSize`. A replay whose
source stays is kept with it. Each batch is deleted all or nothing; when a run in it is linked for
replay during the delete, the batch rolls back, is rechecked and tried again without that run, and
after three such attempts it is logged as a warning and left for a later sweep.

Resuming from a checkpoint adds the same three conditions for `resume_from`, since a run's
checkpoints are deleted with it: a run is kept while a queued entry or a run that is kept resumes
it, and a resumed run and the run it resumed are deleted together once both have expired. A work
queue entry that is no longer queued (cancelled, or dispatched as a run of its own) and names a
deleted run has its `resume_from` cleared, so no row names a run that is gone. A manifest's prune
clears the link on a run or entry outside the manifest, such as an operator's resume, that names one
of the manifest's runs; a queued one then runs from the top.

Cancelled trains are treated as terminal, they are eligible for cleanup but are **not retried** and **do not create dead letters**. Cancellation is an explicit operator action, not a transient failure. A run that stopped because something inside it gave up, such as an `HttpClient` timeout, was not cancelled: it is recorded `Failed` and classified `Transient`, so it counts toward `MaxRetries` like any other failure.

## Dead Letter Auto-Purge

Resolved dead letters (Retried or Acknowledged) are automatically purged after the configured retention period. This is enabled by default.

The `DeadLetterCleanupTrain` runs on its own polling interval (default: 1 hour) and deletes resolved dead letters where `ResolvedAt` is older than `DeadLetterRetentionPeriod` (default: 30 days). Dead letters in `AwaitingIntervention` status are never deleted. Nor is a retried dead letter whose requeued work queue entry is still `Queued` (a paused dispatcher, or a group at its limit): deleting the dead letter would delete that retry before it ran. It is purged on a later run, once the retry has left the queue.

Configure via the scheduler builder:

```csharp
.AddScheduler(scheduler => scheduler
    // Disable auto-purge (dead letters accumulate forever)
    .AutoPurgeDeadLetters(false)

    // Or customize retention
    .DeadLetterRetentionPeriod(TimeSpan.FromDays(90))
)
```

Both settings can also be changed at runtime, from the dashboard's Server Settings page or the [`updateScheduler`](/docs/sdk-reference/graphql-api/mutations#config-nested-namespace) mutation. The purge reads them on every run, so turning auto-purge off keeps resolved dead letters from the next run on, without a restart, and the cleanup service stays registered so it can be turned back on the same way.

Both settings delete data, so when the builder states them and a saved setting does too, they fail closed rather than letting the saved value win:

| Code | Saved | Result |
|------|-------|--------|
| `AutoPurgeDeadLetters(false)` | `true` | No purge |
| `AutoPurgeDeadLetters(true)` or unstated | `false` | No purge |
| `DeadLetterRetentionPeriod(90 days)` | 7 days | 90 days |
| `DeadLetterRetentionPeriod(7 days)` | 90 days | 90 days |

When the builder does not state a setting, a saved value replaces the default as any other saved setting does. In every case where the saved value differs from the host's code value, the scheduler logs a warning naming the setting, both values and the one it runs with.

## Testing

For integration tests, use `UseInMemory()` and the scheduler will automatically use its in-memory job submitter:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UseInMemory())
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);
```

Jobs execute inline, so tests are fast and don't need database infrastructure. The in-memory
provider has no work queue, though, so it does not show retry delays as Postgres runs them, nor
replay decisions. To test retries, dead letters and requeues as they run in production, start the
real host against a test Postgres and wait on the `trax.metadata` and `trax.dead_letter` rows, as
the [Scheduling sample's E2E suite](/docs/samples/scheduling#testing-it) does.

## SDK Reference

> [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [ManifestManagement](/docs/sdk-reference/scheduler-api/manifest-management) | [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler#dead-letters)
