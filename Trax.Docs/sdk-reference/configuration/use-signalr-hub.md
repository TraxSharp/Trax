---
layout: default
title: UseSignalRHub
parent: Configuration
grand_parent: SDK Reference
nav_order: 13
---

# UseSignalRHub

Adds a SignalR sink to the broadcaster pipeline so connected browser, Blazor, or JS clients receive train lifecycle events in real time. Composes alongside cross-process transports like [`UseRabbitMq`](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq), or runs on its own when producer and consumer share a process.

The sink is registered as both an `ITrainLifecycleHook` (so local-process events flow directly) and an `ITrainEventHandler` (so events arriving over a transport like RabbitMQ also reach connected clients). A single singleton dispatcher backs both paths.

## Signature

```csharp
public static BroadcasterBuilder UseSignalRHub(
    this BroadcasterBuilder builder,
    Action<SignalRSinkOptions>? configure = null
)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `builder` | `BroadcasterBuilder` | Yes | The broadcaster builder (inside the `UseBroadcaster` callback) |
| `configure` | `Action<SignalRSinkOptions>?` | No | Filter or projection configuration |

## Options

| Method | Parameters | Description |
|--------|------------|-------------|
| `OnlyForEvents` | `params string[] eventTypes` | Restrict to listed event types (`Started`, `Completed`, `Failed`, `Cancelled`, `StateChanged`). Multiple calls accumulate. Without a call, every event type is allowed. |
| `OnlyForTrains<T1>()` ... `<T1, T2, T3>()` | type parameters | Restrict to listed train interface types. Stores `typeof(T).FullName` (the canonical identifier Trax puts on the wire), not the short name. |
| `OnlyForTrains` | `params Type[]` | Same as the generic overloads. Throws if any type is not an interface. |
| `WithProjection<TClient>` | `Func<TrainLifecycleEventMessage, TClient>` | Replace the default `TraxClientEvent` projection. Last call wins. |
| `WithDeliveryQueueCapacity` | `int capacity` | How many events may wait for delivery to clients. Default `SignalRSinkOptions.DefaultDeliveryQueueCapacity` (1024). Throws `ArgumentOutOfRangeException` below 1. See [Delivery](#delivery). |

## Default projection

When `WithProjection` is not called, every event that passes the filters is projected to a `TraxClientEvent`:

| Field | Type | Source |
|-------|------|--------|
| `MetadataId` | `long` | `TrainLifecycleEventMessage.MetadataId` |
| `ExternalId` | `string` | `TrainLifecycleEventMessage.ExternalId` |
| `TrainName` | `string` | `TrainLifecycleEventMessage.TrainName` (interface FullName) |
| `EventType` | `string` | `Started` \| `Completed` \| `Failed` \| `Cancelled` \| `StateChanged` |
| `Timestamp` | `DateTime` | `TrainLifecycleEventMessage.Timestamp` |
| `FailureReason` | `string?` | `TrainLifecycleEventMessage.FailureReason` (null when not a failure) |

The `Executor`, `TrainState`, `Output`, `HostName`, and `HostEnvironment` fields on the original message are intentionally dropped. Use `WithProjection` to keep them.

## Example

```csharp
builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connectionString)
            .UseBroadcaster(b => b
                .UseRabbitMq(rabbitMqUrl)
                .UseSignalRHub(opts => opts
                    .OnlyForEvents("Completed", "Failed")
                    .OnlyForTrains<ICheckGeocodeDriftTrain, IRunAuditTrain>()))));

var app = builder.Build();
app.MapTraxTrainEventHub();
```

Browsers connect to `/hubs/trax-events` and receive a `TrainEvent` callback for every `Completed` or `Failed` lifecycle event of the listed trains. The example wires RabbitMQ alongside, so events from remote worker processes also reach the browser.

## Custom projection

```csharp
b.UseSignalRHub(opts => opts.WithProjection(msg =>
    new {
        msg.ExternalId,
        msg.TrainName,
        msg.EventType,
        Output = msg.Output  // ship the train's Output through to the browser
    }))
```

The projection is invoked once per matching event, after filtering. The hub serializes the result via SignalR's configured `IHubProtocol` (JSON by default).

## Delivery

The lifecycle hook and the event handler do not wait for clients. They apply the filters and write the event to a bounded queue, then return. One background sender takes events off the queue in the order they were raised, applies the projection, and sends each to `Clients.All`. A train's `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled` and `OnStateChanged` therefore take the same time whether the connected clients are fast, slow or stalled.

Delivery is best effort. `Clients.All` waits for every connection, so a client that cannot keep up slows the sender for everyone until [`MapTraxTrainEventHub`'s send timeout](/docs/sdk-reference/configuration/map-trax-train-event-hub#send-timeout) disconnects it. If clients fall behind by more than the queue's capacity, further events are dropped rather than holding up trains. The first drop logs a `Warning` naming the capacity, and when the queue next empties a second `Warning` gives how many were dropped. A client that must not miss an event reads it from the store or from a durable transport, not from this sink.

The dispatcher is also a hosted service. When the host stops, it stops accepting events and waits for the queued ones to be sent, within the host's shutdown timeout; whatever is left when that timeout runs out is abandoned and logged. The reasoning is recorded in Trax.Effect's ADR 0012.

```csharp
b.UseSignalRHub(opts => opts.WithDeliveryQueueCapacity(4096))
```

## Error handling

If the hub send fails for any reason (e.g. a client disconnects mid-send, a transient transport error), the background sender logs at `Error` level and carries on with the next event. A slow or broken client never throws out of the lifecycle hook or event handler, and never holds them up.

## Local vs remote coverage

The same singleton dispatcher is registered twice in DI:

- As an `ITrainLifecycleHook`, so trains running in the same process fire it directly with no transport hop.
- As an `ITrainEventHandler`, so events arriving via `TrainEventReceiverService` (the broadcaster's transport-side consumer) also flow through.

The `TrainEventReceiverService` skips events whose `Executor` matches the local process, so the dispatcher does not double-fire when both paths exist on one host.

## Prerequisites

- Call `builder.Services.AddSignalR()` on the host. Without it, [`MapTraxTrainEventHub`](/docs/sdk-reference/configuration/map-trax-train-event-hub) throws an `InvalidOperationException` at startup.

## Packages

```
dotnet add package Trax.Effect.Broadcaster.SignalR
```

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
