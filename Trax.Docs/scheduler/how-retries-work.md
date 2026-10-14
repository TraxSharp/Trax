---
title: How retries work
description: "How the scheduler counts a manifest's failures, delays its retries, decides when a retry runs, replays recorded decisions and resumes from checkpoints."
type: explanation
area: scheduling
nav_order: 5
source:
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Utilities/RetryDecisionReplay.cs
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Utilities/RetryResume.cs
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Junctions/CreateWorkQueueEntriesJunction.cs
---

# How retries work

Trax has no per-junction retry. When a manifest's run fails, the scheduler runs the manifest again,
from its first junction, until it succeeds or runs out of retries and is dead-lettered. Only a
manifest's run is retried this way: a train run directly, queued through the operations
`queueTrain` mutation, or queued by a `[TraxMutation]` dispatch mutation with `mode: QUEUE` (or
`GraphQLOperation.Queue`) has no retry, never replays and never becomes a dead letter. A train that
should recover runs from a manifest (`Schedule`, or `ScheduleOnceAsync` for a single run).
[Building a train that recovers](/docs/effect/recover-a-deciding-train) lists every
piece it needs.

## Counting failures

`MaxRetries` is the number of retries after the first run, so a manifest runs at most
`MaxRetries + 1` times before it is dead-lettered: `MaxRetries(0)` dead-letters on the first
failure, and the default of 3 on the fourth. A failure counts only while it started inside the
manifest's failure window (its `FailureWindow`, or the scheduler's `FailureCountWindow`, 24 hours
by default) and after its latest resolved dead letter. So failures weeks apart never add up to a
dead letter, and a requeued manifest starts fresh. A success does not reset the count inside the
window, but it does end the backoff.

A cancelled run, timed out or cancelled by an operator, is not a failure: it is not retried and
does not count. A run that stopped because something inside it gave up, such as an `HttpClient`
timeout, was not cancelled: it is recorded `Failed`, classified `Transient`, and counts like any
other failure. A dispatch attempt that never reached a runner (`DispatchRequeued`) does not count
either; see [JobDispatcher: Dispatch failures](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures).

## Retry delay and backoff

When a manifest's latest finished run failed and its counted failures have not exceeded
`MaxRetries`, the next run, the retry, is delayed by an exponential backoff over the counted
failures:

```
delay = min(DefaultRetryDelay * RetryBackoffMultiplier ^ (failureCount - 1), MaxRetryDelay)
```

With the defaults (5 minutes, 2.0, 1 hour):

| Failure # | Delay |
|-----------|-------|
| 1 | 5 minutes |
| 2 | 10 minutes |
| 3 | 20 minutes |
| 4 | 40 minutes |
| 5+ | 1 hour (capped) |

Only a retry waits. Once a run succeeds or is cancelled, the next occurrence runs on time, however
many failures are still inside the window; those failures still set the next retry's delay and
still count toward the dead letter. The options are on
[AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler), and the scheduler warns at startup
when the backoff for `DefaultMaxRetries` retries alone reaches the failure window, since a manifest
that always fails would then never be dead-lettered.

The delay is the work queue entry's `ScheduledAt`: the JobDispatcher skips an entry until it comes
due. The comparison runs on the database's clock, so `ScheduledAt` is the database's time plus the
delay, not the scheduler host's. A retry with no delay (`DefaultRetryDelay(TimeSpan.Zero)`) stores
no `ScheduledAt` and is dispatched on the next poll, however far the host's clock runs ahead.

## When a retry runs

A retry is not a timer of its own. It is the manifest's next due run, queued with that delay. A
failed run does not move the point a schedule counts from (an interval counts from the last
success, a cron from its last success's occurrence), so a manifest that failed while it was due is
still due, and the ManifestManager queues the retry on its next cycle:

| The failed run was | Its retry is queued |
|---|---|
| A due interval or cron run | On the next ManifestManager cycle, dispatched once the delay passes |
| A dependent's run | On the next cycle: the parent's success it was started for has still not been consumed |
| A one-off's run | On the next cycle, while the one-off has not succeeded |
| Started off-schedule by a trigger, with the manifest not due | Only once the manifest is next due by its schedule; then after the delay |

So with `MaxRetries(2)`, `DefaultRetryDelay(2 s)` and the default multiplier, a due manifest that
keeps failing runs three times within about ten seconds and is dead-lettered on the next cycle. The
[Scheduling sample](/docs/samples/scheduling) runs exactly that. A requeue of the dead letter, by
contrast, queues a run at once, with no backoff; see [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup).

## Retries replay decisions

A retry exists because something after a decision failed: a tool step threw, a database timed out.
A retry runs the chain from its first junction, and asking a model again costs a call per question
and can be answered differently. So when a manifest's run fails, its retry, queued by the
ManifestManager, and a requeue of its dead letter replay the
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
[metadata cleanup](/docs/scheduler/admin-trains/metadata-cleanup) keeps that history whole on every host: it never deletes a
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

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IScoreLeadsTrain>(
                "score-leads",
                new ScoreLeadsInput(),
                Every.Minutes(30),
                options => options.ReplayDecisionsOnRetry(false)
            )
        )
);
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

## On InMemory

The InMemory provider has no work queue: its ManifestManager dispatches inline. It does not delay
retries the way Postgres does and does not replay decisions, so retries are best tested against a
real database, as the [Scheduling sample's E2E suite](/docs/samples/scheduling#tests) does.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler)
