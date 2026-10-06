---
authors: [Theauxm]
areas: [scheduling, providers]
status: accepted
---

# A queued run is refused where nothing dispatches it

On a host with no database provider (the InMemory store), the operations surface refuses
every action that would put a run on the work queue: `QueueTrainAsync`,
`RequeueExecutionAsync`, `TriggerManifestAsync`, `TriggerManifestsAsync` and
`TriggerManifestGroupsAsync`, and `ITraxScheduler`'s dead-letter requeues
(`RequeueDeadLetterAsync`, `RequeueDeadLettersAsync`, `RequeueAllDeadLettersAsync`, which the
requeue-all job runs). Each returns a failed result with
`OperationsService.NoDispatcherMessage` and writes nothing. A caller the train's authorization
refuses is refused for that first, so the refusal never tells such a caller what store the host has. Running a train now
(`RunTrainAsync`) is unaffected.

## Status

**Accepted.**

## Why this is written down

Because the queue looked like it worked. The job dispatcher needs a database and is registered
only with one; the InMemory store is this process's memory, so no other host can dispatch it
either. A queued entry was written, its id returned, and the run never started, with nothing
telling the caller or the operator.

The signal is the store itself: a context from the service's factory that is not relational is
the EF Core InMemory provider. A relational store (PostgreSQL, SQLite) is never refused, so a host
with a database and no local scheduler (an API-only host whose scheduler runs elsewhere) keeps
queueing, because another process can dispatch its rows. The store is asked first, so a service built
by hand with a partial provider is judged by where it writes; a host that registers a database
provider (an `ISqlDialect`) is never refused, which no in-memory host does.

Two answers were weighed. **Refuse at startup** when a queue-capable surface is exposed with no
dispatcher: matches the build-time validators, but a bare `[TraxMutation]` exposes Queue by
default and the dashboard always has its trigger buttons, so every InMemory development host
that never queues would stop starting. **Refuse the request** (this ADR): nothing is written,
the caller is told why and what to do instead, and no host that worked stops starting.

`ITraxScheduler.TriggerAsync`, called from code, is not refused: code on an InMemory host that
triggers (tests, mostly) keeps its behaviour. The operator surfaces go through the operations
service and are refused. The dead-letter requeues are the exception on `ITraxScheduler`: the
dashboard and the GraphQL API call them directly, and the in-memory manifest manager still
dead-letters, so they are refused there. They read the host's provider from the configuration
`AddScheduler()` built, so a scheduler built by hand is not refused.

## Exemplars

- `NoDispatcherRefusalTests` pins the refusal of each queueing operation on InMemory, with
  nothing written and no change signalled, including the dead-letter requeues and the
  requeue-all job; that a caller the train refuses gets the authorization failure instead; and
  that running a train is still allowed.
- [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) states the rule.

**Enforced elsewhere:** the GraphQL per-train `mode: QUEUE` path goes through the mediator, not
this service; Trax.Api refuses it the same way (Trax.Api ADR 0038).

## Changelog

- **2026-10-05**: Recorded.
- **2026-10-05**: The dead-letter requeues are refused too, and a refused caller is authorized
  before the refusal is given.
