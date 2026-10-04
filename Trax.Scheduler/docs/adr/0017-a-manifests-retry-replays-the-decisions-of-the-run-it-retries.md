---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A manifest's retry replays the decisions of the run it retries, once

When a manifest's run fails and the ManifestManager queues its retry, or an operator requeues the
manifest's dead letter, the new entry carries `replay_decisions_of` naming the failed run. The
retry takes the tracks the failed run's deciders chose instead of asking the model again. The
link is set only when replaying is sound, and at most once in a row: answers that were replayed
into a failure are not replayed again. Otherwise the retry asks afresh, and that is never an
error.

## Status

**Accepted.** Extends central `docs/0041`, which until now had a requeue through
`RequeueExecutionAsync` replay and a dead-letter retry and a manifest's own retry not replay. It
also narrows `docs/0041` in one place: a requeue through `RequeueExecutionAsync` of a run whose
answers something already replays asks afresh (below).

## Why this is written down

A retry exists because something after a decision failed: a tool step threw, a database timed
out. Trax has no per-junction retry or checkpoint, so the retry runs the chain from its first
junction. Asking the model again costs a model call for each question, and can be answered
differently, so the retry may do different work from the run it retries with nothing on screen to
say so. Replaying makes the retry repeat what the failed run decided.

What is replayed is the answers, and only those. Every ordinary junction runs again, side effects
included. A train whose junctions are not safe to repeat is as unsafe to retry as before.

## When the link is set

The source is read from the database by `RetryDecisionReplay`, never taken from a caller: no
public scheduler API takes a run to replay, only a `bool` that asks afresh. It is the manifest's
latest finished run, and only when the next run is a retry of it: that run failed and its failure
still counts toward the retries (`FailedCount`, the same test that applies the backoff). A failure
an acknowledged dead letter or the failure window has set aside is not retried, so the next
occurrence asks afresh. The link is set only when all of these hold:

- **The manifest replays decisions on retry** (`replay_decisions_on_retry`, below).
- **The failed run asked its deciders itself, and nothing else replays it.** A failed run that
  was itself a replay, or one that any run (queued, running or finished) or any queued entry
  already replays, is retried afresh. A run Trax.Effect marked `replay_abandoned` asked its
  deciders itself despite its link: its answers are its own and may be replayed once, and it does
  not count as a replay of the run it names: a manifest retry, or a requeue through
  `RequeueExecutionAsync`, which belongs to no manifest. One bad or unusable answer would
  otherwise hold the manifest in a loop of retries, a dead letter and requeues that all repeat it.
  Because the source never replays another run, the replay reads its answers alone: there is no
  chain for the scheduler to walk or for metadata cleanup to keep.
- **For a dependent, its parent has not succeeded again since.** A dependent is fired by its
  parent's success; once the parent succeeds after the failed run was dispatched, the next run is
  a new firing for that success, not a retry, and asks afresh. Both times are the database's: the
  parent's `last_successful_run` is stamped by its clock, and so is the failed run's
  `work_queue.dispatched_at`, which is compared rather than the run's `start_time`, a host's clock.
  A run with no dispatched entry falls back to its start time, as `LoadManifestsJunction` does.
- **It is a run of the manifest's train**, and **it recorded its decisions**
  (`decisions_recorded`) and acted on at least one. A run that did not record may have acted on
  answers nobody can know.
- **The manifest queued it, with the same input.** The work queue entry the run was dispatched
  from must belong to the manifest, carry no subject key, and hold exactly the input and input
  type the retry is queued with (the manifest's `properties` now), compared as stored strings,
  ordinally. Answers were given about an input; a manifest edited between the failure and the
  retry asks afresh. An edit that serializes differently but means the same also asks afresh,
  which costs a question, never a wrong answer, so the comparison is not canonicalized. A run with
  no entry to compare (its entry deleted) asks afresh.

The "nothing else replays it" test is only as good as the history it reads, so metadata cleanup
keeps that history whole: **it never deletes a run that replays another while it keeps the run
replayed.** A replay and the run it replays, both expired, are deleted in the same transaction; a
replay whose source stays is kept with it. Without that, a sweep could delete the replay that
failed with the answers and keep the failed run, which would then look as though nothing had
replayed it. An earlier version guarded this by asking afresh for a failed run past its train's
retention, which depended on the requeuing host being configured with the cleanup's retention: a
GraphQL or dashboard host without `AddMetadataCleanup` skipped the guard. The invariant lives
where the deletes are, so every host reads the same answer.

What the checks guarantee is that the answers replayed were given by this manifest's own failed
run, to this train's questions, about this exact input, and have not already failed once on
replay. They are not a tenant boundary: `manifest.owner` names the application that declared a
manifest, is restamped by whichever application seeds it, and is not checked. Within the replay,
the per-answer fingerprint still applies: a question reworded or offered different options since
is asked afresh.

The lookup runs on a short-lived context of its own, outside the ManifestManager's leader
transaction, and any exception in it is logged and treated as "ask afresh", so a failed lookup
never holds up a cycle or fails a requeue. It is set-based: the ManifestManager looks up every due
retry in one pass, and a dead-letter batch or requeue-all page in one pass, each a fixed number of
queries.

## Asking afresh on purpose

**A manifest can opt out.** `ScheduleOptions.ReplayDecisionsOnRetry(false)` (or
`ManifestOptions.ReplayDecisionsOnRetry` in a batch's `configureEach`, null meaning not stated) is
stored on the manifest as `replay_decisions_on_retry`, default true, and every retry of that
manifest, a dead-letter requeue included, asks afresh. Replaying is the default because a retry
exists to repeat the run; a manifest whose questions should be answered on current information is
the exception the flag is for. It is stored rather than held in a host's configuration so every
scheduler host reads the same answer, for a manifest scheduled at runtime too. A re-seed writes it
only when the code states it (scheduler/0011), and `IOperationsService.SetManifestsReplayDecisionsOnRetryAsync`
sets it at runtime. Code that states it wins on every restart, `ReplayDecisionsOnRetry(true)`
included, and overrides an operator's runtime opt-out, as every stated setting does under
scheduler/0011. Where operators manage the flag at runtime, do not state it in code.

Turning it off reaches a retry already queued: the write clears the link on the manifest's queued
entry, and the dispatcher checks the flag again when it claims an entry, dropping the link of one
whose manifest no longer replays. An opt-out committed in the instant between that check and the
run's start still replays that one run. The race is accepted: closing it would mean locking the
manifest row on every dispatch, and the run it lets through is the one the operator queued before
changing their mind.

**An operator can ask afresh once.** The dead-letter requeues (single, batch and all), the
manifest trigger and `IOperationsService.RequeueExecutionAsync(metadataId, askAfresh, ct)` take
`askAfresh`. A requeue asked afresh queues no link; a trigger asked afresh clears the link of the
queued retry it releases. The trigger's clear is conditional on the entry still being queued, so
when the dispatcher claims the entry first the run replays anyway. Rather than reach into a run
that may already be starting, the trigger reports it: its `ManifestTriggerResult` says the entry
was already dispatched and which run's decisions it replays, and the trigger logs a warning.

**A requeue of an execution keeps the replay-once rule.** `RequeueExecutionAsync` links the run it
re-queues (`docs/0041`), but when a queued entry or a run, in any state, already replays that run,
the requeue asks afresh instead of queueing a second replay of the same answers. It is not
refused: the operator asked for the run to be repeated and gets it, and the result's message says
it asks afresh and why. A refusal was the alternative; it would leave the operator to find the
other replay and wait for it, and the dead-letter requeue already settles the same case by asking
afresh. Either way round the rule now holds: a manifest's retry does not link a run a requeue
replays, and a requeue does not link a run a retry replays. The check and the insert are two
statements, so the database holds the line between them: Trax.Effect's partial unique index
`ix_work_queue_unique_queued_replay` allows one queued entry per replayed run. Every scheduler path
that queues a link (the ManifestManager's retry, the dead-letter requeues, `RequeueExecutionAsync`)
reads that index's violation, and only that one, as "already replayed" and queues the entry to ask
afresh instead: the requeue's message says so as above, and nothing fails. Two hosts, two operators,
or a requeue and a retry landing in the same instant therefore queue one link.

The index covers queued entries only, so it cannot see a replay that has already been dispatched:
a check that ran before the first replay left the queue can still queue a second entry naming the
same run once it has. "At most once" is therefore held where every linked run is created, at
dispatch. The dispatcher, claiming an entry that names a run, first writes that run's row, which
serializes every claim naming the same run until its transaction commits, and then drops the link,
asking afresh, when a run already replays it. A run that abandoned its replay and a dispatch
attempt that failed and was requeued do not count, as above. The same check applies to the failed
run recorded for an entry whose input cannot be read. A row lock rather than an advisory lock
keeps it provider-neutral and keyed to that run alone.

**A replay that never ran still counts as the one replay.** A run whose replay Trax.Effect
abandoned, asking afresh, is marked `replay_abandoned`, and the scheduler reads it as asking afresh
(above). A run queued to replay that never executed (its dispatch exhausted its attempts, its stored
input could not be read, the stale pending reaper failed it) is not marked, keeps its
`replay_decisions_of`, and counts as having replayed the answers; as the manifest's latest failed
run it makes the next retry ask afresh. The answers are lost to that retry; a wrong answer is never
the result. The link is kept deliberately: `docs/0041` follows it when an operator requeues such a
run, to reach the answers it never got to replay.

## Considered options

**Replay only from `RequeueExecutionAsync`.** What `docs/0041` decided. Rejected for retries for
the reason above: the automatic retry is the common case, and it is the one that redid the model's
work.

**Replay every retry, following the chain back.** The first version of this decision. Rejected:
answers that already failed once are the likeliest cause of the next failure, and repeating them
on every retry and every requeue makes a bad answer a trap only an operator can spring.

**Compare canonicalized JSON, or hash the input.** Rejected: both inputs come from the same stored
string, so exact comparison matches whenever the input is unchanged, and any mismatch only asks
afresh.

**Trust the link and let the replay fail when it cannot be honoured.** Rejected: a permanent
failure on a retry is a worse outcome than a fresh question. The checks here keep almost every
unhonourable link from being written, but not all: the source can vanish between the lookup and
the run, and the run can land on a host that does not record decisions. For those the scheduler
relies on Trax.Effect, which asks afresh, with a warning, when a run that belongs to a manifest
names a replay it cannot honour, rather than failing it permanently as it does a caller's
requeue.

## Consequences

**Metadata cleanup deletes a batch all or nothing.** It rechecks the batch, then clears the runs'
work queue entries, logs, dead letter links and children's parent links and deletes the runs in
one transaction, the delete repeating the keep test. When a run was linked after the recheck, the
delete keeps it and the transaction rolls back, so a kept run keeps everything it owns; the batch
is then rechecked and deleted without it. After three attempts the batch is logged as a warning
and left for a later sweep: its runs are excluded from every batch the current sweep selects after
it. A batch takes in the runs it must go with (the runs its replays replay, and the replays of its
runs), so it can be larger than `DeleteBatchSize`. Because the children's parent links and the dead
letters' retry links are cleared inside that transaction, those rows stay locked until the batch
commits, and a child still running waits for it to write its own status. That is the price of the
all-or-nothing rule: cleared beforehand, a batch that rolls back would leave a kept run without its
children and dead letter links.

**A cleanup batch is bounded.** Because a batch is one transaction holding every log, entry and
decision its runs own, `DeleteBatchSize` may not be unbounded: a value above 10,000 fails the
build, and null, which once meant one statement for the whole expired backlog, sweeps in batches
of 10,000.

**The InMemory provider does not replay retries.** Its ManifestManager dispatches without work
queue entries, so there is no queued input to compare, and it does not record the link.

## Exemplars

- `ManifestRetryReplaysDecisionsTests` pins that a retry takes the failed run's tracks without
  asking, that it replays once and the retry after a failed replay asks afresh, that a dead-letter
  requeue after a failed replay asks afresh, that a manifest retry asks afresh once a requeue
  replayed its failed run and failed, that a changed fingerprint re-asks, that every dead-letter
  requeue replays unless asked afresh, that a trigger asked afresh clears a queued retry's link,
  that a manifest that opts out (when scheduled, at runtime, or by a write the dispatcher must
  catch) asks afresh, that a failed lookup queues the retry to ask afresh, that a different input,
  input type, subject key, missing entry, another manifest's entry, another train's run or an
  unrecorded run each ask afresh and complete, that an occurrence after a success or after an
  acknowledged dead letter asks afresh, that a dependent's retry replays until its parent succeeds
  again and then asks afresh, that a dead-letter requeue asks afresh while a queued entry or a
  running run already replays the failed run, that a retry asks afresh once cleanup deleted the
  replay that failed even on a host with no cleanup configured, that a dependent's failed run is
  dated by its dispatch rather than its start, that a requeue of an execution asks afresh while a
  queued retry replays it and when asked to, that two ManifestManagers queueing the same retry at once
  queue one linked entry, the second refused by the one-queued-entry-per-manifest index, that a
  trigger reports an entry the dispatcher claimed first, that the runtime setter counts
  and signals the links it clears, that a requeue, a dead-letter requeue and a retry that each lose
  the race for a queued replay to the unique index ask afresh (the retry inside the leader
  transaction too), that a run whose replay was abandoned
  is a source and is not counted as a replay, that a second entry naming a run whose replay was
  already dispatched, read or unreadable, asks afresh while an abandoned or requeued attempt does
  not count, that a linked retry whose source is deleted before it runs, or that runs on a
  host recording no decisions, asks afresh and completes, and that no public scheduler API accepts
  a run to replay.
- `ReplayDecisionsOnRetrySeedingTests` pins that the opt-out reaches the manifest through every
  way one is scheduled, that a re-seed that does not state it keeps an explicit false, that
  reading it back in `configureEach` does not state it, that a re-seed turning it off clears the
  queued retry's link, and the runtime setter.
- `MetadataCleanupTrainTests.Delete_KeepsARunLinkedAfterItWasSelected` and
  `MetadataCleanupTrainTests.Delete_LinkedMidBatch_KeepsTheRunWithEverythingItOwns` show that a run
  linked after the cleanup selected it, or mid-batch, is kept with its entry, logs, dead letter
  link and child; `Run_DeletesAnExpiredReplayTogetherWithTheRunItReplayed` and
  `Run_KeepsAnExpiredReplayWhileTheRunItReplayedIsKept` pin the invariant above;
  `Delete_LinkedOnEveryAttempt_LeavesTheBatchAndLogsIt`,
  `Run_ABatchLinkedOnEveryAttempt_IsLeftForTheNextSweep` and
  `BatchSize_NullIsCapped_SoOneTransactionNeverHoldsTheWholeBacklog` pin the bound; the rest of
  that class belongs to the cleanup's own rules.

**Enforced elsewhere:** `DecisionRecordingTests` in Trax.Effect pins the replay itself, the
fingerprint check, and that a manifest run's unhonourable replay asks afresh.

Not covered: a change to what a track's junctions do is in no fingerprint and no input, so a retry
after a deploy that changed a junction replays the old answers into the new code.

## Changelog

- **2026-10-03**: A batch left after three attempts was selected again within the same sweep, not
  left for a later one; it is now excluded for the rest of the sweep. The row locks the batch
  transaction holds on children and dead letters are written down.
- **2026-10-03**: "Link once" was held only while the first replay was queued; a second entry
  queued after it was dispatched replayed the same answers again. The dispatcher now checks, under a
  lock on the replayed run's row, that no run already replays it, and asks afresh if one does.
- **2026-10-02**: The same-instant race is closed: Trax.Effect's unique index on queued replays
  holds one queued replay per run, and each scheduler path that hits it asks afresh. A run marked
  `replay_abandoned` asked afresh, so it is a source and not a replay.
- **2026-10-02**: Cleanup never deletes a replay while keeping the run it replays, which replaces
  the retention guard that depended on the requeuing host's configuration; a dependent's failed run
  is dated by its database-stamped dispatch; `RequeueExecutionAsync` asks afresh when the run is
  already replayed, and takes `askAfresh`; a trigger reports an entry the dispatcher claimed first;
  a cleanup batch is capped at 10,000 and a batch left after three attempts is logged; a replay
  that never ran is recorded as a lost replay.
- **2026-10-02**: Only a retry links (an acknowledged or windowed-out failure and a dependent's
  new firing ask afresh); any existing replay of the failed run, in any state, blocks another; a
  failed run past its retention asks afresh; cleanup deletes a batch all or nothing; the opt-out's
  re-seed rule and the dispatch race are spelled out; an unhonourable manifest replay relies on
  Trax.Effect asking afresh.
- **2026-10-02**: A retry replays at most once in a row, so the scheduler no longer walks a
  chain; operators can ask afresh on a requeue or trigger; turning the opt-out off reaches a
  queued retry; the lookup is set-based, isolated and fails open to asking afresh; cleanup keeps a
  run linked after it was selected; the owner is no longer described as a boundary.
- **2026-10-02**: A manifest can opt out of replaying decisions on retry.
- **2026-10-02**: Recorded.
