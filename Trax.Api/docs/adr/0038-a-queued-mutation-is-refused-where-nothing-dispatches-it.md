---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# A queued mutation is refused where nothing dispatches it

A train mutation's `mode: QUEUE` (or a Queue-only train's mutation) on a host whose store is in
this process's memory is refused with `TRAX_QUEUE_UNAVAILABLE`, and nothing is enqueued. The
host still starts, and logs a warning at startup naming the queue-capable train mutations.

## Status

**Accepted.** It departs from [0001](./0001-a-misconfigured-host-fails-at-startup.md) on purpose,
for the reason below.

## Why this is written down

Because the mutation looked like it worked: it answered with a `workQueueId`, and the run never
started. The job dispatcher needs a database, and an in-memory store cannot be reached by a
scheduler in another process, so on such a host nothing ever dispatches the entry.

The test for "nothing dispatches" is a store that is not relational (EF Core's InMemory
provider) on a host that registers no `ISqlDialect`; every database provider registers one. A host with a database and no local scheduler (the API-only topology)
keeps queueing, since a scheduler elsewhere dispatches its rows. A host with no data provider at
all is not refused here: the enqueue fails in the mediator.

Two answers were weighed. **Refuse at startup** (what 0001 asks of a misconfiguration): a bare
`[TraxMutation]` exposes Run and Queue by default, so every in-memory development host with one
would stop starting though it never queues. **Refuse the request** (this ADR): nothing is
written, the caller is told to use `mode: RUN`, and no host that worked stops starting. The
startup warning names the trains so the operator can expose them as Run only.

The train's own authorization is applied before the refusal, through the mediator's
`PrepareAsync`, which writes nothing: a caller the train refuses gets `TRAX_AUTHORIZATION`, the
same answer it gets on any other host, rather than one that describes this host's store.

The operations surface (`queueTrain`, `requeueExecution`, the manifest triggers) is refused the
same way, in the Scheduler's shared operations service (Trax.Scheduler ADR 0019), so the
dashboard and the API answer alike.

## Exemplars

- `QueueWithoutDispatcherTests` pins the refusal on an in-memory store, with nothing enqueued,
  that a caller the train refuses is told it is not authorized instead, and that a host with a
  database still queues.

**Enforced elsewhere:** the operations namespace's queueing mutations are refused by
`OperationsService` (Trax.Scheduler `NoDispatcherRefusalTests`).

## Changelog

- **2026-10-05**: The train's authorization is applied before the refusal.
- **2026-10-05**: Recorded.
