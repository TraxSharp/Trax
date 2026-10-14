---
title: Broadcaster Sinks
description: "How the train event broadcaster splits transports from sinks, how data-change signals travel with train events, and SignalR against GraphQL subscriptions."
type: explanation
area: effects
nav_order: 3
source:
  - Trax.Effect/src/Trax.Effect/Extensions/BroadcasterExtensions.cs
  - Trax.Effect/src/Trax.Effect/Services/TrainEventBroadcaster
---

# Broadcaster Sinks

The broadcaster has two extension points: **transports** carry events between processes, and **sinks** consume events to do something useful with them (forward to a UI, write to a database, etc.).

## Transports

A transport implements both `ITrainEventBroadcaster` (publish side) and `ITrainEventReceiver` (subscribe side). The shipped one is RabbitMQ via [`UseRabbitMq`](/docs/sdk-reference/configuration/use-rabbit-mq). You can write your own (see [UseBroadcaster: Implementing a Custom Transport](/docs/effect/implement-a-broadcast-transport)).

Within a single host, the broadcaster wiring looks like:

- Train completes locally → the lifecycle hook that `UseBroadcaster()` registers publishes via the transport's `ITrainEventBroadcaster`.
- A remote process publishes → that host's `TrainEventReceiverService` receives via `ITrainEventReceiver` and dispatches to every registered `ITrainEventHandler`.

The receiver service skips events stamped with its own host's instance id, so a host that both produces and consumes does not see its own events twice, while replicas of one app still see each other's (see [UseBroadcaster: De-duplication](/docs/sdk-reference/configuration/use-broadcaster#de-duplication)).

## Sinks

A sink is anything that reacts to lifecycle events. The two patterns:

| Sink path | Interface | Fires for |
|-----------|-----------|-----------|
| Local | `ITrainLifecycleHook` | Trains running in the same process |
| Remote | `ITrainEventHandler` | Events received over a transport |

A sink that wants to react to **every** event regardless of where the train ran should register as both. The [SignalR sink](/docs/sdk-reference/configuration/use-signalr-hub) and Trax's built-in GraphQL subscription handler both use this dual-registration pattern.

A purely headless sink (writes to a database, files, an external API) only needs `ITrainEventHandler` when it lives on a host that receives events over a transport.

### Data-change signals

The broadcaster also carries coalesced data-change signals, not just train lifecycle events. A write path calls `ITraxChangeSignal.Notify(domain)`; in a single-process deployment the signal reaches local GraphQL subscribers directly, and when `UseBroadcaster()` is configured a `BroadcastChangeSink` forwards it to other processes over the same transport (the receiving side re-publishes it to its own subscribers). This is what drives the dashboard's `onDataChanged` push without polling. A signal arrives at an `ITrainEventHandler` as a message with `EventType` `DataChanged` and no train fields; the SignalR sink skips it, and a handler that only cares about trains should too. See [Subscriptions: Data Change Signals](/docs/sdk-reference/graphql-api/subscriptions#data-change-signals).

## Pairing a transport with a sink

The usual multi-process layout pairs them: workers publish over a transport, and one host receives and hands events to a sink that reaches clients. [Broadcast train events across processes](/docs/effect/broadcast-train-events-across-processes) sets it up with RabbitMQ and SignalR, and [Write a headless event sink](/docs/effect/write-a-headless-event-sink) writes a sink with no client.

## SignalR and GraphQL subscriptions

Both deliver lifecycle events to clients in real time. They fit different stacks:

- **SignalR** is the native push channel for Blazor Server. It lands inside the same connection Blazor already maintains, so a hub method call updates component state and the framework pushes the DOM diff with no extra protocol.
- **GraphQL subscriptions** are the natural fit when the rest of your API is GraphQL and clients are JS SPAs already speaking that protocol.

The two are not mutually exclusive. Trax registers a separate `GraphQLTrainEventHandler` when `AddTraxGraphQL()` is wired up; the SignalR sink is registered independently. They coexist by both reading from the same broadcaster pipeline.

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
