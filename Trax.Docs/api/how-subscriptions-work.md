---
title: How subscriptions work
description: "How Trax publishes train lifecycle events to GraphQL subscribers in one process and across processes, and why clients refetch after a reconnect."
type: explanation
area: api
nav_order: 22
---

# How subscriptions work

Trax's subscriptions run on HotChocolate's subscription infrastructure with its in-memory pub/sub
transport. `AddTraxGraphQL()` enables them; the fields, payloads and authentication are in the
[Subscriptions reference](/docs/sdk-reference/graphql-api/subscriptions).

## Lifecycle events come from a hook

Subscriptions are powered by the [lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook) system. The `GraphQLSubscriptionHook` is automatically registered by `AddTraxGraphQL()` and publishes lifecycle events to HotChocolate's in-memory subscription transport.

At startup, the hook builds a set of canonical train names (using `ServiceType.FullName`, the interface name) from registrations that have `[TraxBroadcast]`. On each lifecycle event it publishes when the train is opted in. **If the operations surface is exposed, it publishes for every train** (`TrainLifecycleStreamOptions.StreamAllTrains`, set automatically by `AddTraxGraphQL()` when `ExposeOperationQueries()`/`ExposeOperationMutations()` is called).

```
ServiceTrain.Run()
  → LifecycleHookRunner.OnCompleted()
    → GraphQLSubscriptionHook.OnCompleted()
      → Publish if: operations surface exposed (all trains), OR
                    metadata.Name matches a [TraxBroadcast] train's ServiceType.FullName
        → Yes → ITopicEventSender.SendAsync("OnTrainCompleted", event)
          → WebSocket clients receive the event
        → No → skip (no event published)
```

## Across processes

By default, subscriptions only fire for trains that execute in the same process as the GraphQL API. In distributed deployments where trains are queued and executed by separate worker processes, use [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster) to bridge the gap:

```csharp fragment
// Both hub and worker:
effects.UseBroadcaster(b => b.UseRabbitMq("amqp://guest:guest@localhost:5672"))
```

When a broadcaster is configured, `AddTraxGraphQL()` automatically registers a `GraphQLTrainEventHandler` that receives remote lifecycle events from the message bus and forwards them to HotChocolate's subscription transport. It applies the same rule as the local `GraphQLSubscriptionHook`: forward every train when the operations surface is exposed, otherwise only `[TraxBroadcast]` trains (matched by canonical `ServiceType.FullName`), regardless of which process executes them. Events from the local process are de-duplicated automatically.

Data-change signals ride the same bridge. In a single-process deployment (the API collocated with the scheduler), `onDataChanged` works with no broadcaster: signals flow in-process to the subscription topic. When the scheduler runs in a separate process, `UseBroadcaster()` forwards its `Notify` calls over the message bus (a `BroadcastChangeSink`), and the API's `GraphQLDataChangeHandler` re-publishes them to local subscribers. The originating host ignores its own broadcast via the same instance-id de-duplication used for lifecycle events, so replicas of one app still receive each other's signals.

See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for full details.

## No replay, so clients refetch

The server keeps no state per subscriber and replays nothing: an event emitted while a client's
socket is down is not delivered later. A client that has to stay correct across a restart or a
network drop therefore does two things, and the Trax dashboard does both.

It reconnects for as long as the failure is transient. `graphql-ws` gives up after 5 attempts by
default, and the dashboard sets `retryAttempts: Infinity`. Each reconnect re-establishes the active
subscriptions and authenticates again, because the credential rides in `connection_init`. An
auth-rejection close code (4400, 4401, 4403, 4429) is not retried: a retry cannot fix a bad
credential, so the client terminates the socket and opens a new one with a new credential.

It refetches what it shows once the socket is connected again, since whatever changed while it was
down is gone from the feed. While connected, the coalesced `onDataChanged` signal drives the
refetches; the one after a reconnect covers the gap.

## Registration order

Subscription authentication does not depend on whether the `AddTrax*Auth` call comes before or after `AddTraxGraphQL()`. Earlier versions chose the interceptor from what was registered when `AddTraxGraphQL()` ran, so an auth call placed after it left subscriptions accepting every connection while HTTP stayed gated, and a later version refused to start instead. The composite reads the registered schemes once the container is complete, so either order authenticates.

If a token scheme is registered and HotChocolate's own accept-everything interceptor is the active one, the host refuses to start. Only registering `DefaultSocketSessionInterceptor` yourself produces that.

## SDK Reference

> [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
