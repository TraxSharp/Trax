---
layout: default
title: Subscriptions
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 5
---

# Subscriptions

Trax provides real-time GraphQL subscriptions for train lifecycle events. Clients connect via WebSocket and receive events as trains transition through states (started, completed, failed, cancelled).

Subscriptions are powered by HotChocolate's built-in subscription infrastructure with an in-memory pub/sub transport. They are automatically enabled when you call `AddTraxGraphQL()`.

**Only trains decorated with [`[TraxBroadcast]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-subscription-attribute.md %}) emit subscription events.** Trains without the attribute are silently skipped.

## Subscription Fields

All subscriptions return a `TrainLifecycleEvent` payload.

| Field | Description |
|-------|-------------|
| `onTrainStarted` | Fires when a train begins execution |
| `onTrainCompleted` | Fires when a train completes successfully |
| `onTrainFailed` | Fires when a train fails with an exception |
| `onTrainCancelled` | Fires when a train is cancelled via `CancellationToken` |

## TrainLifecycleEvent Payload

```graphql
type TrainLifecycleEvent {
  metadataId: Long!
  externalId: String!
  trainName: String!
  trainState: TrainState!
  timestamp: DateTime!
  failureStep: String
  failureReason: String
}
```

| Field | Description |
|-------|-------------|
| `metadataId` | The database metadata row ID for this execution |
| `externalId` | The external identifier assigned to this execution |
| `trainName` | The canonical train name (the service interface's fully-qualified name, e.g. `MyApp.Trains.IProcessOrderTrain`) |
| `trainState` | The current state of the train (`InProgress`, `Completed`, `Failed`, `Cancelled`) |
| `timestamp` | When the event occurred (end time if available, otherwise current UTC time) |
| `failureStep` | The step that failed (only present on failed trains) |
| `failureReason` | The failure message (only present on failed trains) |

## Examples

### Subscribe to all completed trains

```graphql
subscription {
  onTrainCompleted {
    metadataId
    trainName
    trainState
    timestamp
  }
}
```

### Subscribe to failures

```graphql
subscription {
  onTrainFailed {
    metadataId
    trainName
    failureStep
    failureReason
    timestamp
  }
}
```

### Subscribe to all lifecycle events

Open multiple subscriptions in parallel:

```graphql
# Tab 1
subscription { onTrainStarted { metadataId trainName trainState } }

# Tab 2
subscription { onTrainCompleted { metadataId trainName trainState } }

# Tab 3
subscription { onTrainFailed { metadataId trainName failureStep failureReason } }

# Tab 4
subscription { onTrainCancelled { metadataId trainName trainState } }
```

## WebSocket Connection

Subscriptions use the GraphQL over WebSocket protocol. Connect to the same endpoint as queries and mutations:

```
ws://localhost:5000/trax/graphql
```

In Banana Cake Pop (the built-in GraphQL IDE), subscriptions work out of the box — just write a subscription query and execute it.

For programmatic clients, use any GraphQL client that supports the `graphql-ws` protocol (e.g., Apollo Client, urql, Strawberry Shake).

## Architecture

Subscriptions are powered by the [lifecycle hooks]({{ site.baseurl }}{% link sdk-reference/configuration/add-lifecycle-hook.md %}) system. The `GraphQLSubscriptionHook` is automatically registered by `AddTraxGraphQL()` and publishes lifecycle events to HotChocolate's in-memory subscription transport.

At startup, the hook builds a set of canonical train names (using `ServiceType.FullName` — the interface name) from registrations that have `[TraxBroadcast]`. On each lifecycle event, it checks the train's metadata name against this set and only publishes if the train is opted in.

```
ServiceTrain.Run()
  → LifecycleHookRunner.OnCompleted()
    → GraphQLSubscriptionHook.OnCompleted()
      → Check: does metadata.Name match a [TraxBroadcast] train's ServiceType.FullName?
        → Yes → ITopicEventSender.SendAsync("OnTrainCompleted", event)
          → WebSocket clients receive the event
        → No → skip (no event published)
```

## Cross-Process Subscriptions

By default, subscriptions only fire for trains that execute in the same process as the GraphQL API. In distributed deployments where trains are queued and executed by separate worker processes, use [`UseBroadcaster()`]({{ site.baseurl }}{% link sdk-reference/configuration/use-broadcaster.md %}) to bridge the gap:

```csharp
// Both hub and worker:
effects.UseBroadcaster(b => b.UseRabbitMq("amqp://guest:guest@localhost:5672"))
```

When a broadcaster is configured, `AddTraxGraphQL()` automatically registers a `GraphQLTrainEventHandler` that receives remote lifecycle events from the message bus and forwards them to HotChocolate's subscription transport. Like the local `GraphQLSubscriptionHook`, the remote handler filters by `[TraxBroadcast]` using canonical train names (`ServiceType.FullName`) — only trains with the attribute produce subscription events, regardless of which process executes them. Events from the local process are de-duplicated automatically.

See [UseBroadcaster]({{ site.baseurl }}{% link sdk-reference/configuration/use-broadcaster.md %}) for full details.

## Package

```
dotnet add package Trax.Api.GraphQL
```
