---
layout: default
title: MapTraxTrainEventHub
parent: Configuration
grand_parent: SDK Reference
nav_order: 14
---

# MapTraxTrainEventHub

Maps the Trax SignalR hub at a URL path. Clients connect here to receive train lifecycle events pushed by the [`UseSignalRHub`](/docs/sdk-reference/configuration/use-signalr-hub) sink.

## Signature

```csharp
public static HubEndpointConventionBuilder MapTraxTrainEventHub(
    this IEndpointRouteBuilder endpoints,
    string path = "/hubs/trax-events"
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `endpoints` | `IEndpointRouteBuilder` | Yes | N/A | Typically `WebApplication` |
| `path` | `string` | No | `"/hubs/trax-events"` | URL where clients open the SignalR connection |

## Prerequisites

The host must register SignalR services:

```csharp
builder.Services.AddSignalR();
```

If `AddSignalR()` is missing, `MapTraxTrainEventHub` throws `InvalidOperationException` with guidance at startup. This fails fast before the host accepts requests rather than letting client connections fail later.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connStr)
            .UseBroadcaster(b => b.UseSignalRHub())));

var app = builder.Build();
app.MapTraxTrainEventHub();      // default "/hubs/trax-events"
// or
app.MapTraxTrainEventHub("/internal/trax-events");
app.Run();
```

## Client examples

### Blazor (C#)

```razor
@inject NavigationManager Nav
@implements IAsyncDisposable

@code {
    private HubConnection? _hub;

    protected override async Task OnInitializedAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/trax-events"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<TraxClientEvent>("TrainEvent", async evt =>
        {
            // update component state, then re-render
            await InvokeAsync(StateHasChanged);
        });

        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
```

### JavaScript / TypeScript

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
    .withUrl("/hubs/trax-events")
    .withAutomaticReconnect()
    .build();

connection.on("TrainEvent", (evt) => {
    // evt: { metadataId, externalId, trainName, eventType, timestamp, failureReason }
});

await connection.start();
```

## Payload shape

By default the hub sends a `TraxClientEvent` per matching lifecycle event. The shape is configurable via [`WithProjection`](/docs/sdk-reference/configuration/use-signalr-hub#options). See [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub#default-projection) for the field list.

## SDK Reference

> [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster)
