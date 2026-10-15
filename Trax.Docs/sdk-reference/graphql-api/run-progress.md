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
only on a host where at least one exposed train carries it.

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

A run records its starter as a hash of the caller's principal id
([schema migration 082](/docs/reference/schema-migrations#run-starter-082-and-083-sqlite-042)):

| Started by | Starter |
|---|---|
| A train mutation, `mode: RUN` or `mode: QUEUE` | the caller |
| A state machine's invoking state, user-owned instance | the instance's owner |
| A requeue or resume from the operations surface | the original run's starter |
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
| `train` | The train's field, as the schema serves it. |
| `state` | `QUEUED`, `PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED` or `CANCELLED`; null with a problem. |
| `steps` | The steps recorded so far, in order, at most 500. Empty unless the host calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events). Only an `EffectJunction` records a step. |
| `failure` | Why the run failed, only when a junction threw a `TrainException`; null for any other exception. |
| `problem` | `NOT_FOUND` when the id names no run the caller started of a train that carries the attribute. |

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

`sequence` counts up by one per event. A skipped number means events were lost on the way, from a full buffer or a
step that could not be published in time: read the run again with the query. A refused subscription answers one event
with a `problem` and completes:

| Code | When |
|---|---|
| `NOT_FOUND` | As for the query. |
| `TOO_MANY_SUBSCRIPTIONS` | The caller already follows this run 10 times on this host, or 500 runs in all. A stream's place comes back when it completes or the client unsubscribes. |

An anonymous subscription is refused with `TRAX_AUTHORIZATION`, as other subscriptions are.

## What a step shows

A step carries the same fields as `onJunctionEvent`'s, with the host detail withheld: no input, output or failure
message, no decider type, and a failure's exception type only when it is a `TrainException`. Without `Answers`, a
step's answer and confidence are null, and a step on a decision track is not named.

The train's events are published to this host's subscriptions because of the attribute. A
[`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) subscriber still never sees a train
that does not carry `[TraxBroadcast]` itself.

## Persisted operations

A host that requires persisted operations serves `runProgress` through generated documents, registered like every
train's and machine's. [`trax client`](/docs/reference/cli#frontend-clients-trax-client) writes a typed hook for them.
