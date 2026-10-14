---
title: Watch a queued run
description: "Follow a train queued with mode QUEUE from a GraphQL client: subscribe to onTrainStateChanged, match the externalId, stop at a terminal state."
type: how-to
area: api
nav_order: 20
---

# Watch a queued run

A [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) mutation called with
`mode: QUEUE` returns an `externalId` and a `workQueueId`, but no `metadataId`: the run does not
exist yet. The run the scheduler later dispatches carries the same `externalId` (see
[Queued runs](/docs/sdk-reference/graphql-api/mutations/dispatch#queued-runs)), so a client follows it
through the lifecycle subscriptions.

## Subscribe first, then queue

1. Open the socket and subscribe to `onTrainStateChanged` **before** sending the mutation. There is no replay, and a short train can finish before a subscription opened afterwards is registered.
2. Send the mutation over HTTP and keep its `externalId`. Buffer any events that arrive before the response does.
3. Keep only events whose `externalId` matches. The subscription was opened before the `externalId` existed, so it cannot carry the filter, and the client filters.
4. Stop at the first terminal state: `COMPLETED` (read `output`), `FAILED` (read `failureReason`) or `CANCELLED`.

```graphql
subscription {
  onTrainStateChanged {
    externalId
    metadataId
    trainState
    sequence
    output
    failureReason
  }
}
```

```text
mutation  -> { "externalId": "66b533589bf747f1aac661a5deeea356", "workQueueId": 2 }
event     -> { "externalId": "66b5...a356", "metadataId": 55, "trainState": "IN_PROGRESS", "sequence": 1 }
event     -> { "externalId": "66b5...a356", "metadataId": 55, "trainState": "COMPLETED", "sequence": 2, "output": { ... } }
```

## Subscribe after queueing

When the client cannot open the subscription first, it can subscribe after the mutation returns with the `externalId` as the filter, and receive only that run's events:

```graphql
subscription Follow($externalId: String) {
  onTrainStateChanged(externalId: $externalId) {
    metadataId
    trainState
    sequence
    output
    failureReason
  }
}
```

A run that starts before that subscription is registered sends its earlier events to nobody, and a short run can finish in that window. Give the wait a timeout, and read the outcome another way when it expires: `operations.executions(externalId:)` for an operator, the app's own data otherwise.

## Check what the subscriber needs

What the subscriber needs and what it should expect:

- The train carries `[TraxBroadcast]` and a posture that admits the caller (for example the same `[TraxAuthorize(Roles = ...)]` that lets it call the mutation), unless the caller satisfies the operations authorization. Without it the subscription is refused with `TRAX_AUTHORIZATION`, as [Who receives what](/docs/sdk-reference/graphql-api/subscriptions#who-receives-what) says, even on a host that exposes operations.
- A failed delivery that the dispatcher requeues (`MaxDispatchAttempts`) creates a new execution row with the same `externalId` and emits no event; the client sees events only from the attempt a runner started. A run that fails at dispatch for good, its deliveries exhausted or its stored input unreadable, emits `onTrainFailed` and a `FAILED` `onTrainStateChanged` with its `externalId` and no `IN_PROGRESS` before it, as a train's own failure does. Its `failureException` is `DispatchFailed` and its `failureReason` a fixed sentence pointing at the run's record, so a broadcast subscriber sees the masked reason; the cause is on the execution row, which the operations queries return. Still give the wait a timeout: a lost event, or a host that stops mid-dispatch, leaves the run to the stale-pending reaper, whose failure (`failureException` `StalePendingTimeout`) is emitted the same way but only after its timeout.
- A `sequence` that skips a number means events were lost. An operator can look the run up in `operations.executions`; a client without that access should read the outcome from the app's own data (a [query model](/docs/sdk-reference/graphql-api/query-models) over what the train wrote).
- A queued run that fails is not retried.

A runnable C# version of this loop over `System.Net.WebSockets` is the `GraphQLWebSocketClient` in `tests/Trax.Samples.ChatService.E2E/Utilities` of [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples); see [Testing subscriptions](/docs/cross-cutting/e2e-testing#testing-subscriptions).

## SDK Reference

> [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch) | [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute)
