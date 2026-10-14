---
title: UseRabbitMq
description: "Reference for UseRabbitMq, the RabbitMQ broadcaster transport: its options, exchanges, publisher confirms, queue limits and RabbitMqTrainEventReceiver."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 51
package: Trax.Effect.Broadcaster.RabbitMQ
namespace:
  - Trax.Effect.Broadcaster.RabbitMQ.Extensions
  - Trax.Effect.Broadcaster.RabbitMQ
source:
  - Trax.Effect/src/Trax.Effect.Broadcaster.RabbitMQ/Extensions/RabbitMqBroadcasterExtensions.cs
  - Trax.Effect/src/Trax.Effect.Broadcaster.RabbitMQ/RabbitMqBroadcasterOptions.cs
  - Trax.Effect/src/Trax.Effect.Broadcaster.RabbitMQ/RabbitMqTrainEventReceiver.cs
---

# UseRabbitMq

The RabbitMQ transport for [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster). Publishes lifecycle
events to a fanout exchange and receives them through a queue of the host's own.

## Signature

```csharp fragment
public static BroadcasterBuilder UseRabbitMq(
    this BroadcasterBuilder builder,
    string connectionString,
    Action<RabbitMqBroadcasterOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionString` | `string` | The AMQP connection URI, such as `amqp://guest:guest@localhost:5672` |
| `configure` | `Action<RabbitMqBroadcasterOptions>?` | Changes the options below |

### RabbitMqBroadcasterOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | `string` | N/A | AMQP connection URI |
| `ExchangeName` | `string` | `"trax.lifecycle"` | Fanout exchange name |
| `JunctionExchangeName` | `string?` | `ExchangeName` + `".junctions"` | Fanout exchange [junction events](/docs/effect/junction-events) are published to. Must differ from `ExchangeName`, or `UseRabbitMq` throws `ArgumentException`. |
| `PrefetchCount` | `ushort` | `64` | How many received events the receiver may hold unacknowledged at once. The broker holds the rest until the handlers acknowledge one, so a slow handler leaves events queued on the broker rather than in the receiving process. `0` (no limit) is refused: `UseRabbitMq` throws `ArgumentException`, and a directly constructed receiver throws `InvalidOperationException` from `StartAsync`. |

## Returns

`BroadcasterBuilder`, for chaining a sink such as `UseSignalRHub()`.

## Behavior

Registers the options, the broadcaster as `ITrainEventBroadcaster` and a `RabbitMqTrainEventReceiver` as
`ITrainEventReceiver`, all singletons. The receiver binds the junction exchange only on a host with an
`IJunctionEventHandler` registered.

The RabbitMQ transport uses a **fanout exchange** so all connected hub instances receive every event. Each receiver creates its own exclusive, auto-delete queue. Every process bound to an exchange receives every event published to it, whichever application published it, so two applications on a shared broker with the same `ExchangeName` and vhost receive each other's events.

Publishing never waits on the broker. A lifecycle hook is awaited inside the train, so the broadcaster writes each event to a bounded queue (1024 events) and returns; one background sender publishes the queue in order.

Each publish waits for the broker's publisher confirm, for at most 5 seconds. An event the broker has not confirmed is sent again, so a receiver can occasionally see the same event twice, but an event lost on a connection that died while still reporting open is not counted as sent. An attempt that times out discards the connection as well as the channel. The exchange is declared on every channel the sender opens, so one deleted, or lost with a broker restart, is recreated.

While the broker is unreachable the sender retries the event it holds with a growing delay (1 second, doubling to 30), each connection attempt bounded at 5 seconds, and further events wait in the queue. An event the broker refuses, by closing the channel on it with a channel-level error (`403`, `404`, `405`, `406`) or by rejecting the publish, is tried 3 times, then dropped and logged as an `Error`, so a refusal that does not clear, such as an exchange of the same name declared with another type, does not hold up the events behind it.

When the queue is full, junction events give way first, then non-terminal events. A new junction event is dropped. A new `Started`, `StateChanged` or `DataChanged` event takes the place of the oldest queued junction event, or is dropped when none is queued. A new `Completed`, `Failed` or `Cancelled` event takes the place of the oldest queued junction or non-terminal event, and is dropped itself only when every queued event is terminal. So a run whose `Started` reached subscribers keeps its outcome, although a subscriber can see an outcome without the `Started` before it. The first drop logs a `Warning`, followed by a second one giving the count when the queue drains.

A connection the broker closed is disposed before it is replaced. On shutdown the broadcaster waits up to 5 seconds for queued events to be sent, and does not wait at all while it is failing to reach the broker. Delivery is best effort.

Stopping the receiver tolerates a connection the broker has already closed, for example when the broker restarts or another host sharing it shuts down first. When `TrainEventReceiverService` retries a receiver that failed to start, each new start closes and disposes the connection the previous attempt opened, and a start that fails partway releases what it opened.

### Junction events

On a host that calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events),
each step of a run is published to the junction exchange, `trax.lifecycle.junctions` by default.
It is used only where steps are: a publisher declares it when it first has a step to send, and a
receiver binds it only on a host with an `IJunctionEventHandler` registered, each on a channel of
its own. A junction exchange the broker refuses (one declared elsewhere with another type, say)
drops steps, logged, and never closes the channel train events use. After a failure on the junction
exchange the publisher drops steps untried for a backoff that starts at one second and doubles up to
a minute, then tries the exchange again, so a broken junction exchange costs the sender one attempt
per backoff rather than one per step. A receiver takes train events
only from the train exchange and steps only from the junction exchange, and drops anything that
arrives on the other. A receiver from a Trax version before junction events binds only the train
exchange, so it never receives one.

Hosts of mixed versions share a broker. A host binds the junction exchange once it has a junction
event handler; until one does, the steps a worker publishes reach no receiver. A worker without
`AddJunctionEvents` publishes no steps and its train events are unchanged.

A step and its run's own events travel through different exchanges, so a subscriber can see a
run's `Completed` before its last step. Each step carries its position and timestamps; order by
those.


### RabbitMqTrainEventReceiver

| Member | Description |
|--------|-------------|
| `RabbitMqTrainEventReceiver(options, logger)` | Opens no connection until `StartAsync` |
| `StartAsync(handler, ct)` | Connects, declares the train exchange, binds a new exclusive, auto-delete queue to it (and to the junction exchange, on a channel of its own, when it binds that), and consumes. A delivery is acknowledged after `handler` returns; when the handler or deserialization throws, the error is logged and the delivery rejected without requeue. Throws `InvalidOperationException` when `PrefetchCount` is 0. A second call first closes what the previous one opened. |
| `StopAsync(ct)` | Stops consuming and closes the connection, tolerating one the broker already closed |
| `DisposeAsync()` | Stops and releases the connection |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString, opts =>
            opts.ExchangeName = "my-app.lifecycle"))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster), [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events)
- [Broadcast events between processes](/docs/effect/broadcast-events-between-processes)
