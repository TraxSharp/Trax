---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# An enqueue resolves its train in a scope of its own

`TrainExecutionService.QueueAsync` reads a train's `QueueSubjectKey`, `DeferQueuePromotion` and
`OnQueue` from one instance, resolved on first use in a DI scope the enqueue creates and disposes
before it returns. It does not resolve the train from the caller's scope, and a train that
overrides none of those members is never resolved at all.

## Status

**Accepted.**

## Considered options

**Resolve from the caller's scope.** What the code did, three times per enqueue. The caller's
scope is whatever injected `ITrainExecutionService`, and on the dashboard that is a Blazor
circuit, which lives as long as the browser tab. Every transient train, with its `EffectRunner`
and data context, stayed referenced by that scope until the tab closed. Worse, the scoped services
a hook took were shared by every enqueue from the tab: a hook that tracked an entity on a scoped
`DbContext` and then failed on `SaveChanges` left the entity `Added`, and every later enqueue of
that train from that tab failed on it.

**Create the scope in `OperationsService` or the dashboard.** Fixes one caller and leaves every
other long-lived caller (a hosted service, a singleton holding a scope) with the same leak. The
enqueue owns the train it resolves, so it owns the scope.

**Resolve once per member, each in its own scope.** Keeps three instances per enqueue and lets the
key and the hook disagree about the train's state. One instance per enqueue is both cheaper and
what a train author would assume.

## Consequences

A hook's scoped dependencies are the enqueue's, not the caller's. A hook cannot write into the
caller's request `DbContext` or unit of work and have the caller save it; it writes through
`IEnqueueContextAccessor.Current`, or through its own context with its own commit. What flows
with the async call rather than the scope still reaches the hook: the ambient enqueue context,
the trusted execution scope, and `IHttpContextAccessor`.

Authorization still runs against the caller's scope, before the train's scope exists, because
that is where the caller's identity and trust live.

## Exemplars

- `EnqueueScopeTests` (memory-leak suite) pins one instance per enqueue, disposed before the
  enqueue returns, when the caller's scope outlives it.
- `EnqueueScopedServicesTests` (Postgres suite) pins that a scoped `DbContext` a failed hook left
  dirty does not fail the next enqueue from the same caller scope.
- [OnQueue: enqueue-time hook](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) is the
  rule this produces.

Not covered: the run path is outside this decision. `RunAsync` hands the train to the train bus,
which creates a scope per run of its own accord, and nothing here checks that it keeps doing so.

## Changelog

- **2026-09-27**: Recorded.
