---
title: Run progress
description: "Reference for TraxRunProgress and the runProgress query and subscription, which let the caller who started a run follow it step by step."
type: reference
area: api
package: Trax.Effect
namespace: Trax.Effect.Attributes
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 13
source:
  - Trax.Effect/src/Trax.Effect/Attributes/TraxRunProgressAttribute.cs
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/RunProgress.cs
---

# Run progress

`[TraxRunProgress]` lets the caller who started a run of a train follow it: where it stands, each step as it runs,
and how it ended. The caller reads it once with `query { runProgress(runId) }` and follows it with
`subscription { runProgress(runId) }`. Nobody else can: another caller, an operator and an anonymous one all get
`NOT_FOUND`. The operations view keeps [`onJunctionEvent`](/docs/sdk-reference/graphql-api/subscriptions) for that.

## Signature

```csharp fragment
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TraxRunProgressAttribute : Attribute
{
    public bool Answers { get; init; }
}
```

| Property | Default | Meaning |
|---|---|---|
| `Answers` | `false` | Show the starter a step's answer and confidence, and what ran on a decision track. Off, those are withheld as they are from a [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) subscriber. |

The attribute ships in Trax.Effect, so a train carries it without depending on Trax.Api.GraphQL. The fields exist
only on a host where at least one train carries it. The train need not be exposed: a scheduled or machine-invoked
train is followed the same way, by whoever its runs record as their starter.

## Example

```csharp fragment
[TraxMutation]
[TraxAuthorize]
[TraxRunProgress]
public class ImportOrdersTrain : ServiceTrain<ImportOrdersInput, ImportOrdersOutput>, IImportOrdersTrain
{
    protected override Task<Either<Exception, ImportOrdersOutput>> Junctions() =>
        Chain<FetchOrders>().Chain<SaveOrders>().Resolve();
}
```

## Who started a run

A run records its starter as a hash, never the id itself: `SHA-256("p\0" + principalId)` for a caller, with the
qualified principal id, and `SHA-256("u\0" + userKey)` for a machine instance's owner
([schema migration 082](/docs/reference/schema-migrations#run-starter-082-and-083-sqlite-042)):

| Started by | Starter |
|---|---|
| A train mutation, `mode: RUN` or `mode: QUEUE` | the caller |
| A state machine's invoking state, user-owned instance | the instance's owner |
| A requeue or resume from the operations surface | the original run's starter |
| A run of a manifest scheduled with [`StartedBy`](/docs/sdk-reference/scheduler-api/schedule), retries included | the caller it names |
| A schedule, the dashboard, the operations namespace, a remote runner | nobody |

A run with no starter cannot be followed. The id to pass is the run's `externalId`, which the mutation answers in
both modes.

## The first read

```graphql
query {
  runProgress(runId: "a3f1...") {
    runId
    train
    state
    failure
    steps { position name state startedAt endedAt }
    problem { code message }
  }
}
```

| Field | Meaning |
|---|---|
| `train` | The train's field, as the schema serves it; null for a train the schema does not expose. |
| `state` | `QUEUED`, `PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED` or `CANCELLED`; null with a problem. |
| `steps` | The steps recorded so far, in order, at most 500. Empty unless the host calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events). Only an `EffectJunction` records a step. |
| `failure` | Why the run failed, only when a junction threw a `TrainException`; null for any other exception. |
| `problem` | `NOT_FOUND` when the id names no run the caller started of a train that carries the attribute. |
| `retriedAs` | For a failed run of a manifest, the id of the run that retries it, once the scheduler queued it. Follow that run next. |

The query needs an authenticated caller. A missing id, another caller's run, a run nobody started and a run of a
train without the attribute answer the same `NOT_FOUND`, through the same query, so the answer says nothing about
whether a run exists.

## The stream

```graphql
subscription {
  runProgress(runId: "a3f1...") {
    sequence
    state
    step { position name state }
    failure
    problem { code message }
  }
}
```

The first event is the state the run was in when the subscription started, so a client that subscribes after the run
ended still learns how it ended. After that each event is either a step or a change of state. The stream completes
after the run's last state, with the run's last steps sent before it.

A step is sent when the run takes it, and written for the query on a queue that can run behind, so the stream can send
a step before the query returns it. A client that subscribes while a run is going can therefore miss a step in both:
its read was answered before the step was written, and its stream started after the step was sent. Read the run
again once the first event arrives and again after the stream completes, and keep each step the stream sent over the
read's copy of it. `useRunProgress` does both.

A run followed while still queued is read again every few seconds until it is dispatched. One cancelled before
dispatch publishes no state of its own, so the stream sends `CANCELLED` and completes; one whose entry is gone with no
run recorded answers `NOT_FOUND` and completes. A read that fails is tried again, waiting twice as long each
time up to 30 seconds; after ten failures in a row the stream sends `INTERNAL_ERROR` and completes: subscribe
again to keep following the run.

A failed run of a manifest stays open after its last state until the scheduler queues its retry, then sends one more
event with `retriedAs` and completes. It looks for the retry after one second, then waits twice as long each time, up
to 30 seconds between looks. It completes without one when the manifest is dead-lettered or disabled, or after 30
minutes. [`useRunProgress`](/docs/reference/cli#frontend-clients-trax-client) follows the retry on its own.

`sequence` counts up by one per event. A skipped number means events were lost on the way, from a full buffer or a
step that could not be published in time: read the run again with the query. A refused subscription answers one event
with a `problem` and completes:

| Code | When |
|---|---|
| `NOT_FOUND` | As for the query. |
| `INTERNAL_ERROR` | A queued run could not be read ten times in a row. Subscribe again. |
| `TOO_MANY_SUBSCRIPTIONS` | The caller already follows this run 10 times on this host, or 500 runs in all. A stream's place comes back when it completes or the client unsubscribes. |

An anonymous subscription is refused with `TRAX_AUTHORIZATION`, as other subscriptions are.

## What a step shows

A step carries the same fields as `onJunctionEvent`'s, with the host detail withheld: no input, output or failure
message, no decider type, and a failure's exception type only when it is a `TrainException`. Without `Answers`, a
step's answer and confidence are null, and a step on a decision track is not named.

The train's events are published to this host's subscriptions because of the attribute. A
[`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) subscriber still never sees a train
that does not carry `[TraxBroadcast]` itself.

## Checking the starter from a train

A train that reports on a run, such as a decision journal, answers only the caller who started it with
`IRunStarters` (`Trax.Mediator.Services.RunStarters`), which `AddMediator()` registers:

```csharp fragment
public interface IRunStarters
{
    Task<bool> StartedByCurrentCallerAsync(string runId, CancellationToken ct = default);
    Task<long?> LatestExecutionStartedByCurrentCallerAsync(string runId, CancellationToken ct = default);
}
```

It compares the run's recorded starter with the current caller, as `runProgress` does, and answers false alike for a
missing id, another caller's run and a run nobody started. A train that then reads the execution's own rows takes the
id `LatestExecutionStartedByCurrentCallerAsync` gives rather than the latest under the run id, which another caller's
execution recorded under the same id could be; it is null in every case the first answers false.

## Persisted operations

A host that requires persisted operations serves `runProgress` through generated documents, registered like every
train's and machine's. [`trax client`](/docs/reference/cli#frontend-clients-trax-client) writes a typed hook for them.
