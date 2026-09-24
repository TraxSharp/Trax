---
layout: default
title: Enqueue and Outcome Changes
parent: Reference
nav_order: 18
---

# Enqueue and Outcome Changes

The release that made `Junctions()` the only way to declare a chain also changed how work is
enqueued and how a run's outcome is recorded. None of it needs a version bump beyond the usual
`1.x` update, and most of it compiles unchanged, which is why it is listed here: several changes
only show up at runtime.

The chain declaration change has its own guide,
[Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate). Read it
first if any train overrides `RunInternal` or calls `Activate`.

## Projects scaffolded by an older Trax.Cli

`trax generate` from earlier Trax.Cli versions wrote trains that override `RunInternal` and a
trains project that references `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Mediator` and
`Trax.Scheduler` with `Version="1.*"`. A floating version restores the newest `1.x`, so such a
project picks up this release on its next restore and stops compiling, with nothing changed in
the project itself.

Move each train to `Junctions()` as the
[RunInternal guide](/docs/migration-guides/runinternal-and-activate#overriding-runinternal)
shows, or pin the Trax packages to an exact version until you do. Current Trax.Cli versions
scaffold `Junctions()`.

## `ITrainExecutionService.QueueAsync`

**A new parameter before `ct`.** The signature is now:

```csharp
Task<QueueTrainResult> QueueAsync(
    string trainName,
    string? inputJson,
    int priority = 0,
    DateTime? scheduledAt = null,
    CancellationToken ct = default
);
```

A call that passes the token positionally after `priority`, such as
`QueueAsync(name, json, 5, ct)`, no longer compiles, because `ct` now lands on `scheduledAt`.
Name it: `QueueAsync(name, json, priority: 5, ct: ct)`. An assembly compiled against the old
signature fails with `MissingMethodException` until it is rebuilt, and a class that implements
`ITrainExecutionService` has to add the parameter.

**A JSON `null` input throws `JsonException`.** It used to throw `InvalidOperationException`. The
same applies to `RunAsync`. Code that caught `InvalidOperationException` to detect a bad input
should catch `JsonException`, which a malformed input already threw.

**A null or blank input is read as `{}`.** The parameter is now `string?`. Through the GraphQL
`queueTrain` mutation, a null input used to be stored as null and failed at dispatch; it is now
read as `{}`, and an input type that needs values is refused at enqueue with `JsonException`:
a constructor parameter with no default, as in a positional record, or a `required` member.
`RunAsync` reads a blank input the same way.

**A deferred entry cancelled under its hook throws `QueuedWorkCancelledException`.** It used to
throw a plain `InvalidOperationException`, the same type as an empty subject key. The new type
derives from `InvalidOperationException`, so existing catches still work, and carries
`WorkQueueId` and `TrainName` for a caller that has to compensate for the hook's side-effect.

See [TrainExecution](/docs/sdk-reference/mediator-api/train-execution#queueasync).

## Every caller-built enqueue goes through the mediator

The GraphQL operations mutations and the dashboard used to write the work queue row
themselves. They now call `QueueAsync`, which changes what runs:

| Path | `[TraxAuthorize]` | `OnQueue` | `QueueSubjectKey` | Input size cap |
|------|------|------|------|------|
| GraphQL `queueTrain`, `requeueExecution` | Now enforced | Now fires | Now stamped | Now applied |
| Dashboard queue dialog, execution **Re-queue** button | Not enforced (trusted scope; the dashboard is gated by its host) | Now fires | Now stamped | Now applied |

What to check:

- **An `OnQueue` hook now runs from these paths.** A hook written on the assumption that only
  your own code enqueues (one that is not idempotent, or that expects a request context) now also
  runs when someone queues or re-queues the train from the API or the dashboard.
- **A caller of `queueTrain` or `requeueExecution` may now be refused** with a
  `TRAX_AUTHORIZATION` error for a train whose requirements it does not meet.
- **An API-only host needs `AddMediator`.** `OperationsService` takes `ITrainExecutionService` as
  a required constructor dependency, so a host that registers `OperationsService` itself without
  `AddMediator` cannot resolve it. Where `AddTraxGraphQL` exposes the operations surface, its
  startup validator refuses to start such a host rather than letting `queueTrain` fail at request
  time.
- **`IOperationsService.QueueTrainAsync` can throw `UnauthorizedAccessException`**
  (`TrainAuthorizationException` when the API's authorization is registered). Code calling it
  directly, such as a custom admin page, should handle it; it is not returned as a failed
  `OperationResult`.
- **`requeueExecution` refuses a run with no saved input**, or one whose input was stored as the
  truncation placeholder, rather than re-running it with an empty input.

`Trax.Docs/adr/0017` records why, and
[mutations](/docs/sdk-reference/graphql-api/mutations#queuetrain) lists the failure messages.

## A cancelled caller can get a completed run

A train's terminal state is now saved on a token the caller cannot cancel. A train whose work
finishes after the caller cancelled (because a downstream call ignored the token) is recorded and
returned as `Completed`, where the caller used to get `OperationCanceledException` and the row
stayed `InProgress`. Code that treated a cancelled request as proof the work did not happen should
check the result instead. See
[Cancellation Tokens](/docs/cross-cutting/cancellation-tokens#servicetrain-token-propagation);
`effect/0005` in Trax.Effect records the decision.

## Work queue entries are built only by `WorkQueue.Create`

`WorkQueue`'s parameterless constructor is now `protected`, so `new WorkQueue { ... }` no longer
compiles. It used to compile and then fail silently: an entry built that way has a null
`ConfirmedAt`, which makes it a staged entry the dispatcher never claims and the stale-staged
sweep eventually cancels. Build entries with `WorkQueue.Create(new CreateWorkQueue { ... })`,
which stamps `ConfirmedAt` unless you set `DeferPromotion`.

`WorkQueue.Create` also checks `CreateWorkQueue.SubjectKey` now, the same way an enqueue through
the mediator does. An empty key, or one longer than `WorkQueue.MaxSubjectKeyLength` (512
characters), throws `ArgumentException`. Leave the key null when the entry should not be
serialized.

## Records that gained parameters

These positional records gained optional trailing parameters:

| Record | Package | New parameters |
|--------|---------|----------------|
| `ExecutionSummary` | Trax.Api | `FailureClass` |
| `ExecutionDetail` | Trax.Api | `FailureClass` |
| `WorkQueueSummary` | Trax.Api | `ConfirmedAt`, `SubjectKey` |
| `RemoteRunResponse` | Trax.Scheduler | `FailureClass` |

Constructing one by position still compiles, because the new parameters have defaults.
Deconstructing one positionally (`var (id, name, ...) = summary`) does not, since the generated
`Deconstruct` gained the same parameters, and an assembly compiled against the old constructor
fails with `MissingMethodException` until rebuilt.

## Rolling deploys

Two new train overrides change what a dispatcher must understand, so upgrade every host that
runs the JobDispatcher before any train uses them:

- `DeferQueuePromotion`: an older dispatcher does not check `confirmed_at` and would dispatch a
  staged entry before its hook returns. See
  [Making the side-effect durable](/docs/core/trains-and-junctions#making-the-side-effect-durable).
- `QueueSubjectKey`: an older dispatcher claims without the subject check, so two entries for one
  subject can run at once. See
  [QueueSubjectKey](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing).

Postgres migration 041 adds `work_queue.confirmed_at` and backfills it from `created_at` on every
row that can still be dispatched: queued and cancelled entries, and anything created in the last
day. Older dispatched entries are left null rather than rewritten, because `work_queue` keeps
every dispatched entry until metadata cleanup removes it, and rewriting them all locks and
rewrites the whole table. Nothing reads `confirmed_at` on a dispatched entry, but a report or
query of your own that does will see null there.

## SDK Reference

> [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution) | [Mutations](/docs/sdk-reference/graphql-api/mutations) | [Junctions](/docs/sdk-reference/train-methods/junctions)
