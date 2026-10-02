---
layout: default
title: ITraxTrainEventClient
description: Reference for ITraxTrainEventClient, the typed client surface of the Trax SignalR hub and its single TrainEvent method, with how clients receive events.
parent: Configuration
grand_parent: SDK Reference
nav_order: 17
---

# ITraxTrainEventClient

The strongly typed client surface of the Trax SignalR hub. It names the one method the server calls on a connected client: `TrainEvent`, once per lifecycle event that passes the [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) sink's filters. A client subscribes to that method name; it does not implement the interface.

## Signature

```csharp
namespace Trax.Effect.Broadcaster.SignalR.Services;

public interface ITraxTrainEventClient
{
    Task TrainEvent(object payload);
}

public sealed class TraxTrainEventHub : Hub<ITraxTrainEventClient>
{
    public TraxTrainEventHub();
}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `payload` | `object` | The event as the sink's projection shaped it: a `TraxClientEvent` unless `WithProjection` replaced it |

**Returns**: a task that completes when SignalR has handed the message to the transport.

The payload is `object` so a projection can produce any JSON-serializable shape without a generic parameter on the hub. Clients deserialize it themselves.

## Receiving events

| Client | Subscribe with |
|--------|----------------|
| .NET (`Microsoft.AspNetCore.SignalR.Client`) | `connection.On<TraxClientEvent>("TrainEvent", handler)`, or your projection's type |
| JavaScript / TypeScript (`@microsoft/signalr`) | `connection.on("TrainEvent", handler)` |

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
- Every client the hub admits receives every event that passes the sink's filters. The default `TraxClientEvent` leaves out the failure reason and output for that reason; see [Default projection](/docs/sdk-reference/configuration/use-signalr-hub#default-projection).

## Package

```
dotnet add package Trax.Effect.Broadcaster.SignalR
```
