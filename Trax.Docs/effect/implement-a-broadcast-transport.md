---
title: Implement a broadcast transport
description: How to carry Trax lifecycle events over a message bus other than RabbitMQ by implementing ITrainEventBroadcaster and ITrainEventReceiver.
type: how-to
area: effects
nav_order: 22
---

# Implement a broadcast transport

`UseBroadcaster` hands its events to whatever `ITrainEventBroadcaster` is registered and reads incoming ones from
the registered `ITrainEventReceiver`. A transport for another bus is those two classes and an extension method that
registers them.

## 1. Publish

`PublishAsync` is called on the run's path, from a lifecycle hook the train awaits. Queue the message and return;
send from a background loop.

```csharp compile=transport
using System.Threading.Channels;
using Trax.Effect.Services.TrainEventBroadcaster;

public sealed class MyBusBroadcaster(string connectionString) : ITrainEventBroadcaster
{
    private readonly Channel<TrainLifecycleEventMessage> _queue =
        Channel.CreateBounded<TrainLifecycleEventMessage>(
            new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest });

    public string ConnectionString { get; } = connectionString;

    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        _queue.Writer.TryWrite(message);
        return Task.CompletedTask;
    }
}
```

On a host that calls `AddJunctionEvents()`, the broadcaster also receives every junction event: a message whose
`Junction` is set (`TrainLifecycleEventMessage.IsJunctionEvent(message.EventType)`). Route those apart from train
events, to a topic of their own, so a receiver that predates junction events never sees one.

## 2. Receive

`TrainEventReceiverService` calls `StartAsync` with the handler that dispatches to the host's `ITrainEventHandler`s,
and calls it again after a failed start, with a growing delay, so a second call must release what the first opened.

```csharp compile=transport
using Trax.Effect.Services.TrainEventBroadcaster;

public sealed class MyBusReceiver(string connectionString) : ITrainEventReceiver
{
    public string ConnectionString { get; } = connectionString;

    public Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct) =>
        Task.CompletedTask; // subscribe, and call handler for each deserialized message

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Messages carry the publishing host's `InstanceId`; the receiver service drops a host's own, so a transport delivers
everything it receives.

## 3. Register both

```csharp compile=transport
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;

public static class MyBusBroadcasterExtensions
{
    public static BroadcasterBuilder UseMyBus(this BroadcasterBuilder builder, string connectionString)
    {
        builder.ServiceCollection.AddSingleton<ITrainEventBroadcaster>(new MyBusBroadcaster(connectionString));
        builder.ServiceCollection.AddSingleton<ITrainEventReceiver>(new MyBusReceiver(connectionString));
        return builder;
    }
}
```

The host then calls `effects.UseBroadcaster(b => b.UseMyBus(connectionString))`.

## See also

- [How event broadcasting works](/docs/effect/how-event-broadcasting-works)
- [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq), the shipped transport, for the delivery rules it follows

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events)
