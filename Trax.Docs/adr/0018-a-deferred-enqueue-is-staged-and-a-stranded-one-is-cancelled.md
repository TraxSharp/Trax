---
authors: [Theauxm]
repos: [effect, mediator, scheduler, api, dashboard]
areas: [platform, data-model]
status: accepted
---

# A deferred enqueue is staged, and a stranded one is cancelled

A train whose `OnQueue` hook writes outside Trax's own data context can set
`DeferQueuePromotion`: its work queue entry is committed unconfirmed, the hook runs, and a second
commit confirms it. Dispatch never claims an unconfirmed entry. An entry a crash leaves
unconfirmed is cancelled by the scheduler once it is older than `StaleStagedEntryTimeout`,
unless the host opts into promoting it.

## Status

**Accepted.**

## Considered options

**An outbox, or one transaction across both writes.** A hook writing to another database cannot
share Trax's transaction, and an outbox would move the consumer's side-effect into Trax's
schema. Staging does not make the two writes atomic, but it makes a failure between them
findable: an unconfirmed entry instead of a side-effect with nothing to consume it.

**Promoting stranded entries by default.** Rejected. A stranded entry is ambiguous: the process
may have died after the hook succeeded, before it ran, or after it threw and before the entry
was removed. Only the first is a mutation that was accepted, and nothing recorded tells them
apart. Cancelling keeps the entry visible so a side-effect the hook may have left can be
reconciled. `PromoteStaleStagedEntries()` exists for hosts whose chains re-check what their
hooks checked and whose hooks are idempotent.

**Rejecting nested enqueues.** The ambient enqueue context was a scoped field that refused to
nest, so two enqueues sharing a scope, such as a Blazor circuit, threw. It flows with the async
call instead: each enqueue sees its own context, and one started from inside a hook gets its own
transaction and hands the outer context back. That makes a nested enqueue independent, not part
of the outer one: it commits on its own context and connection before the outer `SaveChanges`, so
it survives if the outer hook later throws or the outer commit rolls back.

## Consequences

Once a hook has returned, confirming the entry does not take the caller's token, and removing
the entry of a hook that threw does not either, for the same reason effect/0005 gives for a
run's outcome: a record that is only written when the caller is still listening is not a record.
`confirmed_at` defaults to `now()` on Postgres so that rows written during a rolling deploy by an
instance that does not know the column are dispatchable. Migration 041 has to set that default
before it backfills existing rows, not after: DbUp runs the statements without a transaction, so an
older instance can insert between any two of them, and a row inserted after the backfill but before
the default existed would be left with a null `confirmed_at`, never dispatched, and cancelled by
the sweep. The column is added bare, then the default is set, then existing rows are backfilled
from `created_at`. The backfill covers only rows that can still be dispatched: queued and
cancelled entries, and entries created in the last day, since a failed dispatch returns its entry
to queued without touching `confirmed_at` and one in flight across the migration must come out
confirmed. Older dispatched entries keep a null nothing reads; rewriting them took 17 s and doubled
the table on 2M rows while locking every row against dispatch. For the same reason
`ix_work_queue_unconfirmed` covers queued rows only. `WorkQueue`'s parameterless constructor is
protected, so `WorkQueue.Create`, which stamps `confirmed_at`, is the only way to build an entry.
A deferring train's hook has no enqueue
context to join, because its entry is already committed.

If the entry is cancelled while its hook runs, by an operator or by the sweep after the hook
outlived `StaleStagedEntryTimeout`, the enqueue throws `QueuedWorkCancelledException` (an
`InvalidOperationException`) rather than reporting success: the work will not run, and the hook's
side-effect may already have landed. If
the sweep promoted it instead (a host that opted in), the entry will run and the enqueue
succeeds. Removing the entry after a hook throws deletes it only while
it is still staged, never once promoted or dispatched, and a failure to remove it does not
replace the hook's exception. Only a train with an `OnQueue` hook opens a transaction for its
enqueue; the common path is a single write. That transaction, and the pooled connection under it,
stays open for as long as the hook runs, so a slow hook on the default path holds a connection to
Trax's database for its whole duration. A deferring train does not: its staging context is
released before the hook runs.

**Every dispatcher must be upgraded before any train sets `DeferQueuePromotion`.** A dispatcher
from before this decision claims without checking `confirmed_at`, so during a rolling deploy it
would dispatch a staged entry whose hook has not returned, or never will.

The sweep runs in the ManifestManager. A deployment where the ManifestManager is disabled
everywhere (`ManifestManagerEnabled = false`) never resolves a stranded entry.

## Exemplars

**Enforced elsewhere:** `DeferredPromotionTests` in Trax.Mediator (staging, removal on a throw,
cancellation before and after the hook, the sweeps, an entry cancelled while its hook ran
failing the enqueue, and a promoted entry surviving a hook that throws), `WorkQueuePromotionTests` in Trax.Effect
(the in-memory provider), `ResolveStaleStagedEntriesJunctionTests` in Trax.Scheduler (cancel by
default, promote when opted in), `SubjectKeySerializationTests` in Trax.Scheduler (stranded
staged entries take no dispatch capacity on either load path), and `EnqueueContextAccessorTests`
in Trax.Effect (nesting and concurrency).

Not covered: nothing checks that a deferring train's hook is idempotent, which promotion relies
on, or that `StaleStagedEntryTimeout` is longer than the slowest hook. The rolling-deploy default
is exercised by `Trax.Effect.Tests.Integration.IntegrationTests.PostgresMigrationTests`, which
inserts rows as an older writer between each of migration 041's statements and requires every one
to end confirmed, and checks which existing rows the backfill touches; that test does not cite
this decision.

## Changelog

- **2026-09-24**: The enqueue of an entry cancelled under its hook throws
  `QueuedWorkCancelledException`, so a caller can tell it from the other refusals.
- **2026-09-24**: Recorded that migration 041 backfills only rows that can still be dispatched,
  that `ix_work_queue_unconfirmed` covers queued rows only, and that `WorkQueue.Create` is the only
  way to build an entry.
- **2026-09-23**: Recorded that migration 041 must set the `confirmed_at` default before the
  backfill, that the default path holds a transaction and connection open for the hook's
  duration, and that an enqueue nested in a hook commits independently of the outer one.
- **2026-09-23**: Recorded.
