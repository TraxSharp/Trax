---
title: How event broadcasting works
description: How Trax carries a run's lifecycle events from the process that ran it to subscribers on other processes, and why a host never delivers its own events twice.
type: explanation
area: effects
nav_order: 23
---

# How event broadcasting works

A run's lifecycle events start inside the process that runs the train, as lifecycle hooks. Subscribers (a GraphQL
subscription, a browser on the SignalR hub) are connected to some process, often not that one. The broadcaster is
the path between them.

```
Worker process                          Hub process
--------------                          -----------
Train.Run()                             GraphQL and SignalR clients
  -> LifecycleHookRunner                  ^
    -> broadcast lifecycle hook           TrainEventReceiverService
      -> ITrainEventBroadcaster             -> ITrainEventHandler (GraphQL, SignalR)
        -> exchange  ------------------>      -> topic / Clients.All
```

The database stays the record. The broadcaster carries notifications: what happened, to which run, and on completion
the output under the stored copy's rules. Everything else (metadata, logs, manifests) is written to and read from the
data provider. A subscriber that must not miss an event reads it from the store; the bus is best effort.

## Two paths into one sink

A sink such as the SignalR dispatcher has to see two kinds of event: runs in its own process, which never touch the
bus, and runs elsewhere, which arrive through it. So one dispatcher is registered twice: as a lifecycle hook for the
local path, and as an `ITrainEventHandler` for events the receiver service hands it. GraphQL subscriptions work the
same way, through `GraphQLSubscriptionHook` locally and `GraphQLTrainEventHandler` for remote events.

That would deliver a local run twice, once directly and once on its way back from the bus. Each host therefore stamps
its messages with an instance id, one per service provider, and the receiver service drops messages carrying its own.
The id is per host rather than per application on purpose: replicas of one app behind a load balancer share an entry
assembly, so filtering by application would drop each replica's events at the others. A message from a publisher too
old to carry an id is always delivered. The `Executor` on a message names the broadcasting process for display only.

## Never waiting on the bus

A lifecycle hook is awaited inside the run, so anything slow in it slows the train. Publishing therefore writes to a
bounded in-process queue and returns, and a background sender talks to the broker. When the queue fills, the events
least worth keeping go first: junction steps, then non-terminal events, and an outcome last, so a subscriber that saw
a run start still learns how it ended. The same reasoning bounds the SignalR sink: a stalled browser can slow
delivery to other browsers until it is disconnected, but never a train.

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub)
