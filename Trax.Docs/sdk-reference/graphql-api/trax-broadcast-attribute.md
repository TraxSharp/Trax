---
layout: default
title: TraxBroadcast Attribute
description: Reference for the TraxBroadcast attribute, which opts a train into GraphQL lifecycle subscription events, locally and through UseBroadcaster.
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 6
---

# TraxBroadcast Attribute

The `[TraxBroadcast]` attribute opts a train into real-time GraphQL [subscription](/docs/sdk-reference/graphql-api/subscriptions) events. Only trains decorated with this attribute will have their lifecycle transitions (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`) published to WebSocket subscribers.

Trains without this attribute run normally but are silently skipped by both the local `GraphQLSubscriptionHook` and the remote `GraphQLTrainEventHandler` (used with [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster)). It filters only those two: a lifecycle hook of your own, registered with [`AddLifecycleHook`](/docs/sdk-reference/configuration/add-lifecycle-hook), runs for every train with or without the attribute, and filters by train name itself (see [Your own subscription fields](/docs/sdk-reference/graphql-api/subscriptions#your-own-subscription-fields)).

The attribute decides what a subscriber **outside the operations view** receives, on every host. If the host exposes the operations (admin) surface via [`ExposeOperationQueries()`/`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql), it publishes every train's events, but only a subscriber that satisfies the operations authorization (the `GateOperations(...)` gate) receives the ones without `[TraxBroadcast]`. Every other subscriber, on that host as on any other, receives only `[TraxBroadcast]` trains whose posture admits it. So a train that end users watch carries `[TraxBroadcast]` even on a host that exposes operations.

A subscriber that no broadcast train admits, and that does not satisfy the operations authorization, is refused when it subscribes. The socket stays open (its `connection_init` was accepted); the subscription gets a `graphql-transport-ws` `error` message and no events:

```json
{ "id": "1", "type": "error", "payload": [{ "message": "Not authorized.", "extensions": { "code": "TRAX_AUTHORIZATION" } }] }
```

A train that end users should watch but that lacks `[TraxBroadcast]` is the usual cause.

A subscriber admitted to some broadcast trains is not refused; it simply never receives events of the trains it may not see. See [Who receives what](/docs/sdk-reference/graphql-api/subscriptions#who-receives-what).

## Definition

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxBroadcastAttribute : Attribute { }
```

A simple marker attribute with no properties, just opt-in/opt-out.

## Example

```csharp
[TraxQuery(Description = "Looks up a player profile")]
[TraxBroadcast]
[TraxAuthorize]
public class LookupPlayerTrain : ServiceTrain<LookupPlayerInput, LookupPlayerOutput>, ILookupPlayerTrain
{
    protected override Task<Either<Exception, LookupPlayerOutput>> Junctions() =>
        Chain<FetchPlayerJunction>().Resolve();
}
```

When `LookupPlayerTrain` completes, subscribers to `onTrainCompleted` that its posture admits receive a `TrainLifecycleEvent` with the train name, state, and timestamp.

## Posture

A broadcast train streams its runs to subscribers, so it states who may receive them, the same way a train exposed as a query or mutation does. On an open endpoint it must carry `[TraxAuthorize]` (optionally with policies or roles) or `[TraxAllowAnonymous]`, or the host does not start. Each event then reaches only the subscribers that posture admits. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#who-receives-what).

The posture admits a subscriber to **every run** of the train, not only the runs it started. A completion carries the run's output, so every caller the train admits sees every run's output. Do not broadcast a train whose output belongs to one user or one room, such as a chat message or a private result: publish it to a subscription field of your own that checks the subscriber against the run (see [Your own subscription fields](/docs/sdk-reference/graphql-api/subscriptions#your-own-subscription-fields)), or keep the output out with [`ExcludeOutput`](/docs/sdk-reference/configuration/save-train-parameters) or [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive).

## Combined with TraxQuery / TraxMutation

`[TraxBroadcast]` and `[TraxQuery]`/`[TraxMutation]` are independent. You can use any combination:

| Attributes | GraphQL fields generated? | Subscription events? |
|-----------|---------------------|---------------------|
| Neither | No | No |
| `[TraxQuery]` or `[TraxMutation]` only | Yes | No |
| `[TraxBroadcast]` only | No | Yes |
| `[TraxMutation]` + `[TraxBroadcast]` | Yes | Yes |

## Discovery

The `[TraxBroadcast]` metadata is available through `ITrainDiscoveryService`. Each `TrainRegistration` includes `IsBroadcastEnabled`. The `trains` GraphQL query also exposes this field.

## Package

```
dotnet add package Trax.Effect
```

The attribute is defined in `Trax.Effect` so train classes can use it without depending on `Trax.Api.GraphQL`.
