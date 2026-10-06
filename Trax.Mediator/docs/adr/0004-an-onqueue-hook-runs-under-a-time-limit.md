---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# An OnQueue hook runs under a time limit

An enqueue whose train overrides `OnQueue` opens its transaction, and with it takes a pooled
connection, before the hook runs, and holds both until the hook returns. A hook that waits on
something slow therefore pins a connection for as long as it waits, and a few of them can empty
the pool for every other enqueue. The enqueue now gives the hook `MaxQueueHookDuration`, 30
seconds by default and set with `WithMaxQueueHookDuration(TimeSpan)`. Past it the hook's token is
cancelled, the enqueue stops waiting whether or not the hook stops, rolls back everything written
on its context, releases the connection, and throws `QueueHookTimeoutException`.

## Status

**Accepted.**

## Considered options

**Begin the transaction lazily**, at the first nested join or at the final `SaveChanges`. It frees
the connection during a slow hook without a limit, but a hook that flushes on
`IEnqueueContextAccessor.Current` would then write in autocommit, and a hook that throws after
flushing would leave its write behind. `QueueHookTransactionTests` pins that rollback, and keeping
it was the deciding constraint.

**Only document `DeferQueuePromotion`.** A deferring train holds no connection while its hook
runs, and that remains the answer for a hook that must wait. It is advice, though: nothing stops a
non-deferring hook from pinning a connection indefinitely.

## Consequences

**A hook that ignores its token keeps running after its enqueue failed.** The enqueue cannot stop
it, only stop waiting for it. From then on it has no enqueue: its context is disposed, so a write
through `Current` fails, and an enqueue it starts is refused instead of committing on its own,
because the caller was told the outer one failed. A side-effect it writes elsewhere can still land
after the failure; that is the same exposure as a hook that throws after writing elsewhere. What it
throws afterwards is logged.

**A caller that cancels loses the hook the same way.** The enqueue awaits the hook on a token linked
to the caller's, so a cancelled caller (a GraphQL client disconnecting, say) stops the wait at once
whether or not the hook stops. The enqueue then treats a hook still running exactly as it treats one
past the limit: the transaction rolls back, an enqueue the hook starts afterwards is refused, and
what it throws afterwards is logged. Letting that enqueue join instead was rejected, because the
transaction it would join has already been rolled back.

**A hook that blocks its thread is not interrupted until it yields.** The limit is enforced by
awaiting the hook with a deadline, which needs the hook to return a task first.

**The default is a behaviour change.** A hook that ran longer than 30 seconds used to succeed and
now fails its enqueue. `Timeout.InfiniteTimeSpan` restores the old behaviour; a deferring train is
the better fix.

**Only hooks that hold a connection are limited.** A deferring train's hook runs after its entry
committed, holds nothing, and is bounded by the stale-staged sweep instead. A train enqueued from
inside another hook runs within that hook's time, so the outer limit covers it.

## Exemplars

- `QueueHookTimeLimitTests` (Postgres suite) pins that two hooks which never return fail their
  enqueues and free a pool of two for an unrelated enqueue, that a write the hook flushed before
  the limit is rolled back, that the hook's token is cancelled, that an enqueue a hook starts after
  the limit is refused, and that a hook within the limit is unaffected.
- `CancelledEnqueueHookTests` (Postgres suite) pins that an enqueue a hook starts after its caller
  cancelled is refused and leaves no row.
- [OnQueue: enqueue-time hook](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) is the
  rule this produces.

Not covered: a hook that blocks its thread without yielding.

## Changelog

- **2026-09-30**: A caller's cancellation abandons a hook that is still running, as the limit does.
- **2026-09-27**: Recorded.
