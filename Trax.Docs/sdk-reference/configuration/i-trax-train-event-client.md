---
layout: default
title: ITraxTrainEventClient
description: Reference for ITraxTrainEventClient, the typed client surface of the Trax SignalR hub, its TrainEvent and JunctionEvent methods, and how clients receive them.
parent: Configuration
grand_parent: SDK Reference
nav_order: 17
---

# ITraxTrainEventClient

The strongly typed client surface of the Trax SignalR hub. It names the methods the server calls on a connected client: `TrainEvent`, once per lifecycle event that passes the [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) sink's filters, and `JunctionEvent`, once per step of a run when the sink is configured with [`WithJunctionEvents()`](/docs/sdk-reference/configuration/use-signalr-hub#junction-events). A client subscribes to that method name; it does not implement the interface.

## Signature

```csharp
namespace Trax.Effect.Broadcaster.SignalR.Services;

public interface ITraxTrainEventClient
{
    Task TrainEvent(object payload);
    Task JunctionEvent(object payload);
}

public sealed class TraxTrainEventHub : Hub<ITraxTrainEventClient>
{
    public TraxTrainEventHub();
}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `payload` | `object` | For `TrainEvent`, the event as the sink's projection shaped it: a `TraxClientEvent` unless `WithProjection` replaced it. For `JunctionEvent`, a `TraxJunctionClientEvent` unless `WithJunctionProjection` replaced it. |

**Returns**: a task that completes when SignalR has handed the message to the transport.

The payload is `object` so a projection can produce any JSON-serializable shape without a generic parameter on the hub. Clients deserialize it themselves.

## Receiving events

| Client | Subscribe with |
|--------|----------------|
| .NET (`Microsoft.AspNetCore.SignalR.Client`) | `connection.On<TraxClientEvent>("TrainEvent", handler)`, or your projection's type; `connection.On<TraxJunctionClientEvent>("JunctionEvent", handler)` for steps |
| JavaScript / TypeScript (`@microsoft/signalr`) | `connection.on("TrainEvent", handler)`; `connection.on("JunctionEvent", handler)` for steps |

The hub is mapped with [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub), at `/hubs/trax-events` by default; that page has complete client examples.

## Example

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://app.example.com/hubs/trax-events",
        o => o.AccessTokenProvider = () => tokens.GetAsync())
    .WithAutomaticReconnect()
    .Build();

connection.On<TraxClientEvent>("TrainEvent", evt =>
    Console.WriteLine($"{evt.TrainName} {evt.EventType} ({evt.ExternalId})"));

await connection.StartAsync();
```

## Remarks

- `TraxTrainEventHub` defines no methods a client can invoke. It only pushes.
- `JunctionEvent` is sent only by a sink configured with `WithJunctionEvents()`, `WithJunctionAnswers()` or `WithJunctionProjection()`, on a host that calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events).
- Every client the hub admits receives every event that passes the sink's filters. The default `TraxClientEvent` leaves out the failure reason and output for that reason; see [Default projection](/docs/sdk-reference/configuration/use-signalr-hub#default-projection).

## Package

```
dotnet add package Trax.Effect.Broadcaster.SignalR
```
