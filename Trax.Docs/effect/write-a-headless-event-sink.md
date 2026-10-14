---
title: Write a headless event sink
description: "React to train lifecycle events without a UI, such as storing a train's output elsewhere, with an ITrainEventHandler and optionally a lifecycle hook."
type: how-to
area: effects
nav_order: 14
source:
  - Trax.Effect/src/Trax.Effect/Services/TrainEventBroadcaster/ITrainEventHandler.cs
---

# Write a headless event sink

A headless sink writes events somewhere other than a client: a database, a file, an external API. This one stores the report a train returns into a repository of your own. What sinks are is on [Broadcaster sinks](/docs/effect/broadcaster-sinks#sinks).

## 1. Handle the events

```csharp compile=sink
using System.Text.Json;
using Trax.Effect.Services.TrainEventBroadcaster;

internal sealed class GeocodeDriftPersister(IGeocodeDriftRepository repo) : ITrainEventHandler
{
    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (message.EventType != "Completed") return;
        if (message.TrainName != typeof(ICheckGeocodeDriftTrain).FullName) return;
        if (string.IsNullOrEmpty(message.Output)) return;

        var report = JsonSerializer.Deserialize<GeocodeDriftReport>(message.Output)!;
        await repo.SaveAsync(report, ct);
    }
}
```

Filter on `EventType`: the broadcaster also delivers data-change signals (`DataChanged`, with no train fields) to every `ITrainEventHandler`.

## 2. Register it

```csharp compile=sink
using Trax.Effect.Services.TrainEventBroadcaster;

builder.Services.AddSingleton<ITrainEventHandler, GeocodeDriftPersister>();
```

An `ITrainEventHandler` receives events that arrive over a transport, so the host needs [`UseBroadcaster`](/docs/sdk-reference/configuration/use-broadcaster) with one.

## 3. Also react to local runs (if the host runs trains)

A handler sees only remote events. If the same host runs the train and must react there too, register the sink as an `ITrainLifecycleHook` as well ([AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)); the SignalR sink is registered both ways for this reason.

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
