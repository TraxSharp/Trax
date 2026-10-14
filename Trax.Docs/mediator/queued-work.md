---
title: How queued work stays durable and serialized
description: "Why an OnQueue side effect can outlive its work queue entry, how deferred promotion and subject keys close the gaps, and what bounds each guarantee."
type: explanation
area: mediator
nav_order: 11
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainExecutionService.cs
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Junctions/ResolveStaleStagedEntriesJunction.cs
---

# How queued work stays durable and serialized

A queued mutation is accepted in one place and run in another, later. Two problems follow from
that gap, and the [enqueue hooks](/docs/mediator/enqueue-hooks) are how a train deals with them: a
side effect performed at acceptance can lose the work that was meant to reconcile it, and two
mutations for the same record can run at the same time.

## Accepting a mutation is two writes

`OnQueue` exists for side effects that should be visible the moment a mutation is accepted, rather
than when its run gets to it: an optimistic write that persists a provisional row, say, which the
real run reconciles later. The hook's write and the work queue row are two writes. If the process
dies between them, the side effect is left with no queued work to consume it.

Where the hook writes decides how that gap is closed.

**Through Trax's own context.** The enqueue exposes the data context it is about to commit on, as
`IEnqueueContextAccessor.Current`. Whatever the hook tracks there commits with the work queue row or
not at all. This is the strong form, and it costs nothing extra, but it covers only entities in
Trax's model, and it holds a pooled connection and an open transaction on Trax's database for as
long as the hook runs. A slow hook, one calling a remote API, holds both for that long, which is
why a non-deferring hook runs under a time limit.

An enqueue started from inside the hook joins the outer one for the same reason: one transaction
means the nested entry commits with the outer entry or is rolled back with it, and it needs no
second connection. Sharing a transaction is also why a nested failure fails the outer enqueue
even when the hook catches it: the failed attempt may have left writes on the shared context.
`Trax.Mediator/docs/adr/0003` records the reasoning.

**Through your own `DbContext`.** EF shares a transaction only between contexts that share a
connection, so a separately registered context commits on its own and cannot be rolled back with
the entry. Nothing can make two writes to two databases atomic. Deferred promotion makes the
failure findable instead: the entry is committed unconfirmed, the hook runs, and a second commit
confirms it. A crash between them leaves an unconfirmed entry rather than an invisible side effect,
and the entry's context is released before the hook runs, so a slow hook holds no connection.

Deferral is opt in because it costs a round trip and earns nothing when the hook writes nowhere
Trax's transaction cannot reach. Immediate promotion is the default, and a train that does not
override `OnQueue` never stages anything.

### Resolving an entry a crash left unconfirmed

An entry a crash left unconfirmed has to be resolved by something, and the ManifestManager does it
on each cycle once the entry is older than `StaleStagedEntryTimeout`. It cancels by default, because
nothing recorded tells a hook that succeeded from one that never ran or one that rejected the
mutation, and only the first is a mutation that was accepted. The cancelled entry stays visible, so
a side effect the hook may have left can be found and reconciled. A host whose deferring trains
re-check in their chain whatever the hook checked, and whose hooks are idempotent, can opt into
promotion instead with `PromoteStaleStagedEntries()`.

Two consequences. The timeout has to stay well above the slowest hook, because an entry resolved
while its hook is still running is resolved wrongly. And the sweep runs in the ManifestManager, so
at least one host sharing the database must run it. `Trax.Docs/adr/0018` records why.

A dispatcher from before deferral claims entries without checking `confirmed_at`, so during a
rolling deploy it would dispatch a staged entry before its hook returned. Every host that runs the
JobDispatcher is upgraded before any train overrides `DeferQueuePromotion`.

## Two mutations for one record

Queued entries are claimed by several workers at once, keyed on nothing, so two mutations for the
same record can run at once. Against a system that resolves concurrent writes by last-write-wins,
that is a lost update. A train that returns a `QueueSubjectKey` says what its work touches, and the
dispatcher will not claim an entry whose subject has a run in flight. The entry stays queued and is
picked up on a later cycle, so nothing is skipped.

The key is an opaque string, compared exactly across all trains. That keeps it cheap to compute
and to compare in the claim, and lets two trains that write the same record serialize against each
other on purpose. The price is that a key not prefixed with something the train owns collides with
any other train's. A key that cannot be computed fails the enqueue rather than becoming null,
because null would drop the guarantee at exactly the moment the caller relied on it.

The key and the queue priority both come from the caller, so a caller allowed to queue a keyed
train chooses which subject it serializes against and where it sits. A key scoped per tenant, or a
record authorized in `OnQueue`, stops a caller naming a subject that is not theirs.

Serialization is enforced in the claim, not only when candidates are picked. Two entries for one
subject are two rows, so row locks do not make them contend, and while both are still queued
neither can see a dispatched sibling to refuse itself. On Postgres the claim takes an advisory lock
on the subject first, held only for the claim, which commits before the job is submitted.
[Multi-Server Concurrency](/docs/scheduler/concurrency#subject-serialization-advisory-lock-per-subject)
has the mechanics, and `Trax.Docs/adr/0019` the reasoning.

### What bounds subject serialization

Only queued work carries a key. Work from a manifest is not about one record and has none, and a
synchronous run never consults the key. A nightly batch can therefore run at the same moment as a
queued run for a record it touches. When a scheduled job and queued mutations write the same
records, either the write is guarded in the database (an EF concurrency token, or
`SELECT ... FOR UPDATE` in a transaction), so the loser fails with a conflict instead of losing an
update, or the manifest's train only finds the records and queues one keyed entry per record. That
enqueue has no HTTP caller, so it runs inside a
[trusted execution scope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) or the
per-record train's `[TraxAuthorize]` refuses it.

A subject is busy until its run reaches a terminal state, which ties the guarantee to whatever
writes a terminal state for a run that did not finish on its own: the stale-run reapers in the
ManifestManager, and the scheduler's startup recovery, which fails every `InProgress` run in the
shared database that started before the recovering host did, whichever host was running it. Neither
can tell a dead worker from a slow one. A run still working when the reaper's timeout passes, or
when another host restarts, is failed, the subject is released, and the next entry runs alongside
it. Serialization holds only while none of those intervene, so the stale timeouts (or the
manifest's `Timeout`) have to stay above the longest run of any train that sets a subject, and a
scheduler restart releases every subject whose run was in progress.

Two cases hold a subject longer. A train excluded with `ExcludeFromMaxActiveJobs<T>()` is skipped by
both reapers, so a dead worker's subject stays held until a scheduler host restarts. And where the
ManifestManager is disabled on every host, only a restart releases a subject.

A dispatcher from before subject keys claims without the check or the lock, so every host that runs
the JobDispatcher is upgraded before any train overrides `QueueSubjectKey`.

## See also

- [Enqueue hooks](/docs/mediator/enqueue-hooks): the members, defaults and refusals.
- [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher), [ManifestManager](/docs/scheduler/admin-trains/manifest-manager).
