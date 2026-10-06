---
authors: [Theauxm]
areas: [data-model, platform]
status: accepted
---

# Cancelled staged entries are deleted after a retention

`IWorkQueuePromotion.CancelStaleAsync`, the sweep that cancels a deferred enqueue a crash left
unconfirmed (central `0018`), also deletes the unconfirmed entries an earlier call cancelled once
they are older than the sweep's window plus 30 days. Without it those rows stay in `work_queue`
forever: they never ran, so they have no metadata, and metadata cleanup is the only other thing
that deletes queue entries.

## Status

**Accepted.**

## Considered options

**A separate delete method that the scheduler calls.** The honest name, and the obvious shape.
Rejected because Trax.Effect runs no periodic work of its own: the method would sit uncalled until
a Trax.Scheduler release invoked it, so the fix would need two releases in order and a pin bump
between them. Folding the delete into the method the scheduler already calls on every
ManifestManager cycle fixes it in one.

**Deleting on the pass that cancels.** Rejected: the cancelled entry is the only record of which
enqueue vanished, kept so a side-effect its hook may have left can be reconciled (central `0018`).
The sweep's warning logs a count, not the entries.

**Making the retention configurable.** Nothing Trax.Effect already exposes reaches this service,
and the scheduler's options live downstream of it. A fixed value was taken over new public API for
a record that accrues only when a host dies mid-enqueue.

## Consequences

**A method called Cancel also deletes.** The name predates this and is kept so the scheduler needs
no change. The XML docs on `IWorkQueuePromotion.CancelStaleAsync` say so, and its return value
still counts only the entries it cancelled.

**The retention is 30 days, counted from `created_at`.** Thirty days is the scheduler's default
dead-letter retention, the other record Trax keeps for an operator to act on; the stale timeouts
(ten minutes to an hour) are about how long to wait before acting, not how long evidence is useful.
Nothing records when an entry was cancelled, so the age is measured from creation, with the
window added so an entry is always cancelled before it can count as expired. The delete runs
before the cancel in each call, so no entry is cancelled and deleted by the same call; but a sweep
that has not run for longer than the retention deletes what it cancels on its very next call.

**It needs an index.** `work_queue` keeps dispatched rows until metadata cleanup removes them, so
the delete, which runs every cycle, would otherwise scan the table. Migration
`045_work_queue_cancelled_staged_index.sql` (SQLite `010`) adds a partial index over just the
cancelled, unconfirmed rows.

**It also deletes an entry an operator cancelled while its hook ran**, once past the retention. That
entry is unconfirmed and cancelled too, never ran, and is the same kind of record.

A host that opted into `PromoteStaleStagedEntries()` never calls `CancelStaleAsync`, so the entries
an operator cancelled mid-hook on such a host are not deleted. They are rare enough not to justify
a second call.

## Exemplars

- `PostgresWorkQueuePromotionTests` cancels 1,000 staged entries through the sweep, and checks that
  the next call deletes those past the retention, keeps those inside it, and leaves a confirmed
  entry an operator cancelled.
- `SqliteWorkQueuePromotionTests.cs` and `WorkQueuePromotionTests.cs` run the same case on SQLite
  and the in-memory provider.

Not covered: nothing checks that the new index is used; the Postgres plan was read by hand when it
was added.

## Changelog

- **2026-09-27**: Recorded.
