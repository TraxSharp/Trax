---
layout: default
title: Broadcaster Sinks
parent: Effect
nav_order: 6
---

# Broadcaster Sinks

The broadcaster has two extension points: **transports** carry events between processes, and **sinks** consume events to do something useful with them (forward to a UI, write to a database, etc.). Knowing which one you need keeps wiring sane on multi-process topologies.

## Transports

A transport implements both `ITrainEventBroadcaster` (publish side) and `ITrainEventReceiver` (subscribe side). The shipped one is RabbitMQ via [`UseRabbitMq`](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq). You can write your own (see [UseBroadcaster: Implementing a Custom Transport](/docs/sdk-reference/configuration/use-broadcaster#implementing-a-custom-transport)).

Within a single host, the broadcaster wiring looks like:

- Train completes locally → `BroadcastLifecycleHook` publishes via the transport's `ITrainEventBroadcaster`.
- A remote process publishes → that host's `TrainEventReceiverService` receives via `ITrainEventReceiver` and dispatches to every registered `ITrainEventHandler`.

The receiver service skips events whose `Executor` matches the local process, so a host that both produces and consumes does not see its own events twice.

## Sinks

A sink is anything that reacts to lifecycle events. The two patterns:

| Sink path | Interface | Fires for |
|-----------|-----------|-----------|
| Local | `ITrainLifecycleHook` | Trains running in the same process |
| Remote | `ITrainEventHandler` | Events received over a transport |

A sink that wants to react to **every** event regardless of where the train ran should register as both. The [SignalR sink](/docs/sdk-reference/configuration/use-signalr-hub) and Trax's built-in GraphQL subscription handler both use this dual-registration pattern.

A purely headless sink (writes to a database, files, an external API) only needs `ITrainEventHandler` when it lives on a host that receives events over a transport.

## Pairing a transport with a sink

This is the canonical multi-process layout: workers and the UI host share a RabbitMQ exchange; the UI host adds SignalR so browsers see events live.

**Worker (Program.cs):**

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connStr)
            .UseBroadcaster(b => b.UseRabbitMq(rabbitMqUrl))));
```

The worker publishes lifecycle events; it has no UI and registers no sinks beyond the broadcaster's own publish hook.

**Hub (Program.cs):**

```csharp
builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connStr)
            .UseBroadcaster(b => b
                .UseRabbitMq(rabbitMqUrl)
                .UseSignalRHub(opts => opts.OnlyForEvents("Completed", "Failed")))));

var app = builder.Build();
app.MapTraxTrainEventHub();
```

The hub subscribes to the RabbitMQ exchange and rebroadcasts every matching event to connected browsers. The same hub also handles trains it runs locally, which fire the SignalR sink directly without a transport hop.

## When to use SignalR vs GraphQL subscriptions

Both deliver lifecycle events to clients in real time. Use the one that matches the rest of your stack:

- **SignalR** is the native push channel for Blazor Server. It lands inside the same connection Blazor already maintains, so a hub method call updates component state and the framework pushes the DOM diff with no extra protocol.
- **GraphQL subscriptions** are the natural fit when the rest of your API is GraphQL and clients are JS SPAs already speaking that protocol.

The two are not mutually exclusive. Trax registers a separate `GraphQLTrainEventHandler` when `AddTraxGraphQL()` is wired up; the SignalR sink is registered independently. They coexist by both reading from the same broadcaster pipeline.

## Headless sink example

A persister that shreds `Output` into a local SQLite database:

```csharp
internal sealed class GeocodeDriftPersister : ITrainEventHandler
{
    private readonly IGeocodeDriftRepository _repo;

    public GeocodeDriftPersister(IGeocodeDriftRepository repo) => _repo = repo;

    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (message.EventType != "Completed") return;
        if (message.TrainName != typeof(ICheckGeocodeDriftTrain).FullName) return;
        if (string.IsNullOrEmpty(message.Output)) return;

        var report = JsonSerializer.Deserialize<GeocodeDriftReport>(message.Output)!;
        await _repo.SaveAsync(report, ct);
    }
}

builder.Services.AddSingleton<ITrainEventHandler, GeocodeDriftPersister>();
```

This sink only reacts to remote events arriving over the transport. If the persister host also runs trains locally and needs to react there too, register the same instance as an `ITrainLifecycleHook` as well (the SignalR sink shows the pattern).

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
