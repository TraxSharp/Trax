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

Trains without this attribute run normally but are silently skipped by both the local `GraphQLSubscriptionHook` and the remote `GraphQLTrainEventHandler` (used with [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster)).

The attribute governs the **user-facing** subscription surface. If the host exposes the operations (admin) surface via [`ExposeOperationQueries()`/`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql), it is treated as an observability host and streams **every** train regardless of this attribute. `[TraxBroadcast]` only matters on hosts that do not expose operations, where it picks the subset of trains that end users are allowed to watch.

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
