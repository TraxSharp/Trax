---
title: Broadcast train events across processes
description: "Pair a RabbitMQ transport with a SignalR sink so that runs on worker processes reach browsers connected to a hub host."
type: how-to
area: effects
nav_order: 13
source:
  - Trax.Effect/src/Trax.Effect.Broadcaster.RabbitMQ
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR
---

# Broadcast train events across processes

Workers and a UI host share a RabbitMQ exchange, and the UI host adds a SignalR sink so browsers see runs live, wherever they ran. What transports and sinks are is on [Broadcaster sinks](/docs/effect/broadcaster-sinks).

## 1. Publish from the workers

```csharp compile
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connectionString)
            .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))));
```

A worker has no UI and registers no sink beyond the broadcaster's own publish hook.

## 2. Receive and rebroadcast on the hub host

```csharp compile
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connectionString)
            .UseBroadcaster(b => b
                .UseRabbitMq(rabbitMqConnectionString)
                .UseSignalRHub(opts => opts.OnlyForEvents("Completed", "Failed")))));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireAuthorization("TraxEvents"));
```

The hub subscribes to the exchange and rebroadcasts each matching event to the browsers its posture admits ([MapTraxTrainEventHub: Authorization](/docs/sdk-reference/configuration/map-trax-train-event-hub#authorization)). Trains the hub host runs itself reach the SignalR sink directly, without a transport hop. The `TraxEvents` policy is yours to define with `AddAuthorization`.

## 3. Check it

Run a train on a worker and watch a connected client receive its `Completed` or `Failed` event. The [SignalR Broadcaster sample](/docs/samples/signalr-broadcaster) is a single-process version with a browser page; the [Energy Hub sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers) is the multi-process case, with GraphQL subscriptions in place of SignalR.

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub)
