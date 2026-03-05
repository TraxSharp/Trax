---
layout: default
title: TraxBroadcast Attribute
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 6
---

# TraxBroadcast Attribute

The `[TraxBroadcast]` attribute opts a train into real-time GraphQL [subscription]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}) events. Only trains decorated with this attribute will have their lifecycle transitions (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`) published to WebSocket subscribers.

Trains without this attribute run normally but are silently skipped by the `GraphQLSubscriptionHook`.

## Definition

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxBroadcastAttribute : Attribute { }
```

A simple marker attribute with no properties — just opt-in/opt-out.

## Example

```csharp
[TraxQuery(Description = "Looks up a player profile")]
[TraxBroadcast]
public class LookupPlayerTrain : ServiceTrain<LookupPlayerInput, LookupPlayerOutput>, ILookupPlayerTrain
{
    protected override async Task<Either<Exception, LookupPlayerOutput>> RunInternal(LookupPlayerInput input) =>
        Activate(input).Chain<FetchPlayerStep>().Resolve();
}
```

When `LookupPlayerTrain` completes, subscribers to `onTrainCompleted` will receive a `TrainLifecycleEvent` with the train name, state, and timestamp.

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
