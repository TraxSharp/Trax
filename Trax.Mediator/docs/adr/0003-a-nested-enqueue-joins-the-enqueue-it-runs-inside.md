---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A nested enqueue joins the enqueue it runs inside

An enqueue started from inside another train's `OnQueue` hook, while that outer enqueue has its
transaction open, writes its entry into the outer enqueue's context and transaction. It commits
with the outer entry or not at all. Where no such transaction exists (a deferring train's hook,
which runs after its own entry committed, or a provider without transactions), the nested enqueue
commits on its own, as every enqueue did before.

This reverses what central `0018` recorded under "Rejecting nested enqueues", that a nested
enqueue is independent and survives the outer rollback. That paragraph now points here.

## Status

**Accepted.**

## Considered options

**Keep nested enqueues independent.** What the code did. Train A's hook enqueues train B and
then throws: A's caller is told the mutation was refused, and B runs anyway. It also cost a
second pooled connection per nesting level while the outer one sat idle in its transaction, so
a few concurrent hooks that enqueue could exhaust a small pool and time each other out (the
Postgres pool test in the exemplars reproduced it with a pool of two).

**Join through `IEnqueueContextAccessor.Current`.** The Effect accessor is the public contract
for a hook's own writes, and a deferring hook is documented to see null there, yet what that
hook enqueues should still join. The Mediator keeps its own async-local record of the running
enqueue instead, and joins only one that writes through the same data context factory.

## Consequences

**A deferring train enqueued from inside a hook is not staged.** Its entry is invisible outside
the outer transaction until that commits, so there is no window between hook and confirm for
staging to protect, and a staged row inside someone else's transaction would only be confirmed by
the same commit anyway. It is written confirmed, and its hook runs as a deferring hook would:
it is meant to see no enqueue context. Clearing the outer context around it needs an API
Trax.Effect does not have yet, so until then that hook still sees the outer context.

**A nested enqueue that fails fails the outer enqueue, even when the hook catches it.** The failed
attempt may have tracked or flushed writes on the shared context, and committing those would be a
partial nested enqueue. The outer enqueue throws an `InvalidOperationException` naming the hook,
with the nested failure as its inner exception.

**A hook must await the enqueues it starts.** One still running when the hook returns fails the
outer enqueue, because the outer cannot commit without knowing whether it succeeded. One that only
begins after the hook has returned has nothing left to join, and commits on its own.

**The nested entry is flushed early.** It needs its id to return, so the nested enqueue calls
`SaveChanges` on the shared context inside the open transaction. That also flushes whatever the
outer hook had tracked so far, which the transaction still rolls back. Nested enqueues a hook runs
concurrently are serialized on the shared context; their hooks are not.

**The in-memory provider has no real transaction.** Its enqueue still joins, but a failed outer
enqueue there can leave the rows a nested enqueue flushed. Only a relational provider makes the
join atomic.

## Exemplars

- `NestedEnqueueTests` (Postgres suite) pins the rollback, the commit, a nested deferring train,
  a swallowed nested failure, concurrent nested enqueues, an unawaited one, and a deferring hook
  having nothing to join.
- `HookConnectionPoolTests` (Postgres suite) pins that two hooks which enqueue and then wait do
  not exhaust a pool of two connections.
- [OnQueue: enqueue-time hook](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) is the
  rule this produces.

Not covered: a hook that runs a nested enqueue in a second container resolves a different data
context factory and does not join, and nothing checks the consumer did not mean it to.

## Changelog

- **2026-09-27**: Recorded.
