---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard]
areas: [platform, data-model, graphql]
status: accepted
---

# A requeued run replays the decisions of the run it repeats

Re-queueing an execution, from the dashboard or through `requeueExecution`, queues a run that
replays the decisions the original recorded: it takes the tracks the original took instead of
asking its deciders again. Both surfaces call `IOperationsService.RequeueExecutionAsync`, which is
the only place a caller can have the link set: to the run being re-queued, and only when that run has decisions
to replay (it recorded one, or was itself queued to replay another run). The link travels from the
queued entry to the new run's metadata as `replay_decisions_of`, and a run with recorded answers
for a question uses them in place of a decider's, following the link back through every run it
repeats. A run's answers are replayed once: a requeue of a run that a queued entry or another run
already replays still queues it, but asks afresh and says so (`scheduler/0017`), and a caller can
ask afresh on purpose with `askAfresh`. A manifest's automatic retry and a requeue of its dead
letter replay the failed run's decisions too, under the checks `scheduler/0017` lists and at most once in a row. An ordinary
enqueue (`queueTrain`, `QueueTrainAsync`) and a manifest's scheduled run that is not a retry do not
replay. Whatever path names a run to replay, a recorded answer is replayed only into a state that
hashes as the state it was given about (`core/0004`), and only while it is younger than
`ReplayAnswersFor`, 24 hours by default (`effect/0020`).

## Status

**Accepted.** Builds on [0040](./0040-a-chain-declares-every-track-and-a-decider-chooses-one.md).

## Why this is written down

Because the alternative is the obvious one, and it is wrong for the reason people requeue.

A re-queue repeats a run, usually because something downstream of a decision failed. Asking a
model again can get a different answer to the same question, so the repeat would take a
different track and do different work from the run it was meant to repeat, with nothing on
screen to say so. Replaying makes the repeat do what the original decided.

## Considered options

**Ask afresh on every run.** Simplest, and what a question no run in the chain reached still
does.
Rejected as the default for a re-queue for the reason above.

**Key the replay by input.** Rejected: two runs with the same input are not the same run, and a
train that is run often with one input would replay a decision from an unrelated run.

**Carry the link in the existing `parent_id`.** Rejected: `parent_id` means a nested child run,
and the run views filter on it, so a re-queued run would vanish from the root-level lists.

## Consequences

**The link is a column on `work_queue` and on `metadata`, not a foreign key.** The original may
be deleted before the re-queue runs (manifest pruning, or a delete outside Trax). A run whose
original no longer exists fails before its first junction, classified permanent, rather than
asking afresh. Metadata cleanup keeps a run while a queued entry or a run it keeps still names it,
so its age alone never breaks a chain, and keeps a run that replays another while it keeps the run
it replays, so a kept run never looks unreplayed. A replay and the run it replays that have both
expired are deleted together, in one batch (`scheduler/0017`).

**Retries replay, once.** A manifest's retry, queued by the ManifestManager, and a requeue of its
dead letter carry `replay_decisions_of` naming the manifest's failed run (`scheduler/0017`). The
scheduler reads the source from the database, never from a caller, and links it only when the
manifest's `replay_decisions_on_retry` is on (the default), the failed run asked its deciders itself
rather than replaying another run, it is a run of the manifest's train that recorded its
decisions, and it was queued by the manifest with the input the retry is queued with. Anything
else asks afresh, which is never an error. Answers that were replayed into a failure are not
replayed again, so one unusable answer cannot hold a manifest in a loop of retries and requeues.
An operator can ask afresh on purpose: the dead-letter requeues and the manifest trigger take
`askAfresh`. A manifest's run whose replay cannot be honoured when it runs (the run it names was
deleted, the host does not record decisions, an answer cannot be read) asks afresh with a warning,
because the scheduler queued it, not a caller who asked for the original's decisions; a manual
requeue still fails, as below. Such a run is marked `replay_abandoned`, and a later replay of it
stops there rather than going on to the run it named, whose answers it never acted on. It counts
as having asked its deciders itself: the next retry may replay its own answers, and it does not use
up the replay of the run it names. A replay that never started (its dispatch was exhausted, its
input could not be read, or the stale-pending sweep failed it) still counts as the run's one
replay: its answers are not offered to the next retry, which asks afresh. A replay can be lost this
way, but an answer is never replayed twice. At most one queued entry replays a given run, held by a
unique index; a link that index refuses, on any path, is queued again without it and asks afresh,
and a requeue's message says so.

**An answer replays only into the same state, and only while it is fresh.** Each recorded
answer carries the hash of the state it was given about
(`DecisionMade.StateHash`, a digest over every field of the state's runtime type, keyed under the
host's `StateHashKey` when it has one, stored as `state_hash`), and Trax.Core replays it only when
the state
asked about now hashes the same (`core/0004`); a different or missing hash asks afresh, with the
reason in `ReplayRefused`. The journal also replays an answer only while it is younger than
`ReplayAnswersFor` (24 hours by default), measured from when a decider gave it, following replayed
rows back to that one, so a replay never makes an answer younger (`effect/0020`). An answer recorded
before `state_hash` existed has no hash and is asked afresh.

**A requeue of a requeue replays the first run.** A run that failed before reaching a question
recorded nothing for it, yet the run it replayed did. The replay follows `replay_decisions_of`
back, and for each question and occurrence the nearest run's answer wins, since that is what the
nearest run acted on. The chain is followed at most 32 runs back.

**An answer replays only into the asking it was given to.** Each recorded answer keeps the
fingerprint Trax.Core reported for it (0040). A chain that changed so that another step asks the
question first, or a question reworded or offered different options, gives a different
fingerprint, and the question is asked afresh rather than answered with something given to a
different question.

**Recording is what makes replay possible.** Decisions are written to `trax.decision` by
`AddDecisionRecording`, against the run, as each is made and before the train acts on it, through
a short-lived data context of its own. A run killed mid-way (an out-of-memory kill, a timeout, a
deploy) keeps every decision it acted on, so its re-queue can repeat them. The journal is a
`Required` observer: a decision that cannot be written fails its step, classified transient,
because acting on a decision a later replay cannot see would make the replay ask afresh.

**A replay that cannot be honoured fails the run**, unless the run belongs to a manifest (see
*Retries replay, once*). A run that names an original to replay fails
before its first junction, classified permanent, when the host does not record decisions, or a run
in its chain does not exist, belongs to another train, or ran without recording its decisions, or
the chain loops or runs past its limit, or a recorded answer cannot be read; and classified
transient when the database fails while loading it. It does not quietly ask again, because the run
was queued to repeat the original. To tell a run that reached no questions from one whose answers
were never recorded, each run's metadata carries `decisions_recorded`, set on its first write. Two
things are still asked afresh: a question no run in the chain reached, and a recorded answer whose
fingerprint differs or that no longer fits the question as it is asked now (an option renamed or
removed, a scale with fewer levels). The second is noted in the stored answer as
`replay_refused`. A recorded choice of a member the step has no track for is not refused: it
replays and takes the fallback track again. A live answer the run refused is recorded too, with
the reason in `refused`, but never replayed and never counted as a decision to replay, so the
requeue of a run that failed on one asks afresh.

**A host that does not record decisions refuses to start.** Every host that runs trains refuses
to start, naming its deciding trains, when `AddDecisionRecording` is missing. A replay on such a
host would already fail rather than ask afresh, but only once a requeue landed on it, and a
warning on one worker of a fleet is easy to miss; Trax fails closed at startup instead. A host
that runs deciding trains must record their decisions, whether or not it ever runs a requeue.

**An execution service that cannot carry the link is a misconfiguration.** A custom or decorating
`ITrainExecutionService` that predates the `QueueTrainOptions` overload throws
`DecisionReplayNotSupportedException` when asked to queue a replay, and the requeue logs and
throws it rather than reporting it as a refused enqueue or queueing without the link.

**A re-queue reads back the input the run was given.** The saved input carries the reference
metadata `SaveTrainParameters()` writes (`$id`, `$values`, `$ref`), which a caller's input does not
honour. The requeue resolves it to the plain tree first (`TrainInputReader.ResolveSavedInput`), so
a list or a repeated object comes back as it was rather than refused or at its defaults.

**The run is identified by its row.** A run started from a row the scheduler created takes that
row's external id, so the decisions it records and the failure data it carries name the run they
belong to. Before this, such a run kept the random id its train instance was constructed with.

**Changing the queue API without breaking it.** `ITrainExecutionService` gained an overload that
takes `QueueTrainOptions`, with a default implementation that refuses a replay rather than
dropping it. `IOperationsService` gained `RequeueExecutionAsync`, with a default implementation
that throws, so an implementation written before it still compiles. `QueueTrainInput` does not
carry the link: the requeue owns it.

## Exemplars

**Enforced elsewhere:** `DecisionRecordingTests` in Trax.Effect pins that a run records each
decision, its fingerprint and every track taken on it against its metadata, that a decision is
written before the run ends, that a decision that cannot be written fails its step as transient,
that a decision not followed records why, that a refused or missing answer is a `refused` row
whose requeue asks afresh, that a decision made after code in the run lost its async flow is still
recorded against the run when exactly one run matches, that a run that fails after deciding still records,
that a run naming an earlier one replays its decisions without asking, that a requeue of a
requeue replays the run before it with the nearer run's answer winning, that an answer whose
fingerprint differs is not replayed, that a replay of a run that does not exist, belongs to
another train, did not record its decisions, has a damaged answer, or whose chain loops or runs
too long fails the run as permanent, that a recorded run that reached no questions asks afresh,
that questions the original never reached are asked afresh, that `HasDecisionsToReplay` is true
for a run that recorded one or replays another, and that a run started from a row takes the row's
external id; `SqliteDecisionRecordingTests` and `PostgresDecisionRecordingTests` run the same
against real stores. `QueueInputTests` in Trax.Mediator pins that the options overload stores the
link and that an implementation predating it throws `DecisionReplayNotSupportedException`;
`SavedInputRoundTripTests` pins that a saved input with lists and repeated objects reads back as
the input the run was given. `OperationsServiceRequeueTests` in Trax.Scheduler pins that a run
that recorded decisions is re-queued to replay them, that one that recorded none is re-queued as
an ordinary enqueue, that `QueueTrainInput` has no property for the link, and that a replay
through an execution service without the overload is a misconfiguration;
`OperationsServiceTests` pins that an ordinary `QueueTrainAsync` enqueue replays nothing, and
`QueueTrainAuthorizationTests` in Trax.Api that the `queueTrain` input does not accept a replay
link; `JobDispatcherTrainTests` pins the link onto the dispatched run's metadata, and
`UnreadableQueuedInputTests` that an unreadable requeue still carries it onto its failed run.
`RequeueReplayEndToEndTests` in Trax.Scheduler queues, requeues, dispatches and runs a deciding
train, and pins that the requeue takes the original's tracks without asking, including through a
requeue that recorded none or only part of its decisions. `MetadataCleanupTrainTests` and
`SqliteCleanupTests` pin that cleanup keeps a run a queued requeue or a kept run will replay, keeps
a replay while the run it replays is kept, and deletes an expired replay together with the run it
replays. `DecisionRecordingStartupCheckTests` pins that a host
without decision recording refuses to start naming its deciding trains, that one that records, or
whose trains do not decide, starts, and that every host that runs trains has the check once and
first. `OperationsQueriesTests` in Trax.Api and `MetadataRequeueRefusalTests` and
`MetadataRequeueTrustedScopeTests` in Trax.Dashboard pin that both re-queue surfaces go through
the shared requeue.
`DecisionRuntimeTests` in Trax.Core pins that an answer replays only into a state that hashes the
same, and that a different or missing hash asks afresh with the reason in `ReplayRefused`;
`DecisionRecordingTests` in Trax.Effect that the hash is stored and an answer older than
`ReplayAnswersFor` is asked afresh; `ManifestRetryReplaysDecisionsTests` in Trax.Scheduler that a
manifest's retry and a dead-letter requeue replay the failed run once, that `askAfresh` and the
manifest's opt-out ask afresh, that a dead-letter requeue asks afresh while something already
replays the failed run, and that each check that fails asks afresh rather than failing.

Not covered: a train whose chain changed between the original run and its re-queue replays the
askings whose fingerprints still match and asks the rest afresh, but a change to what a track's
junctions do is not in any fingerprint; nothing flags that the replayed run took a path the
original chain could not have.

## Changelog

- **2026-10-02**: A run's answers are replayed once: a requeue of a run something already replays
  asks afresh, and `requeueExecution` takes `askAfresh`; a manifest's run that abandons its replay
  is marked `replay_abandoned`, ends a later replay's chain and counts as having asked afresh; one
queued entry at most replays a run; cleanup deletes a replay only
  together with the run it replays; the state hash is keyed under a host's `StateHashKey`.

- **2026-10-02**: A manifest's run whose replay cannot be honoured asks afresh with a warning,
  while a manual requeue still fails; the state hash covers every field of the state's runtime type.

- **2026-10-02**: A manifest's retry and a dead-letter requeue replay the failed run's decisions,
  at most once in a row and only under the checks of `scheduler/0017`; a recorded answer replays
  only into a state that hashes the same (`core/0004`) and only while younger than
  `ReplayAnswersFor` (`effect/0020`); Trax.Core joins the repos bound by the decision.

- **2026-10-02**: A replay follows the chain of requeues back, matches answers to askings by
  fingerprint, and fails on a run of another train or one that did not record its decisions;
  cleanup keeps a run something still replays; the requeue resolves the saved input's reference
  metadata; a host that runs deciding trains without recording refuses to start; a refused live
answer is recorded but never replayed; the
exemplars name the tests that pin the queue input's missing link, the end-to-end requeue and the
cleanup rule.
- **2026-10-01**: Decisions are written as each is made rather than when the run finishes; a
  replay that cannot be honoured (no recording on the host, no such run) fails the run instead of
  asking afresh; the link is set only by `IOperationsService.RequeueExecutionAsync`, not by the
  queue input.
- **2026-10-01**: Recorded.
