---
layout: default
title: UseBroadcaster
description: Reference for UseBroadcaster, which carries train lifecycle events between processes over a transport such as RabbitMQ, so subscriptions see remote runs.
parent: Configuration
grand_parent: SDK Reference
nav_order: 12
---

# UseBroadcaster

Enables cross-process lifecycle event broadcasting. When trains execute on a remote worker process, their lifecycle events (started, completed, failed, cancelled) are published to a message bus and delivered to hub processes, where [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions) forward them to connected clients.

Without `UseBroadcaster()`, subscriptions only fire for trains that execute in the same process as the GraphQL API. With it, subscriptions work regardless of which process executes the train.

## Signature

```csharp
public static TBuilder UseBroadcaster<TBuilder>(
    this TBuilder builder,
    Action<BroadcasterBuilder> configure
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.UseBroadcaster(...)`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `builder` | `TBuilder` | Yes | The effect configuration builder |
| `configure` | `Action<BroadcasterBuilder>` | Yes | Callback to select a transport (e.g., `UseRabbitMq()`) |

## What It Registers

| Component | Description |
|-----------|-------------|
| Broadcast lifecycle hook | Internal lifecycle hook that publishes events to `ITrainEventBroadcaster` |
| Broadcast change sink | Internal sink that forwards coalesced data-change signals (`onDataChanged`) to other processes over the same transport |
| `TrainEventReceiverService` | `BackgroundService` that consumes events from `ITrainEventReceiver` and dispatches train events to `ITrainEventHandler` instances, and [junction events](/docs/effect/junction-events) to `IJunctionEventHandler` instances only |

The transport-specific `ITrainEventBroadcaster` and `ITrainEventReceiver` are registered by the callback (e.g., `UseRabbitMq()`). The hook and the sink are internal types: `UseBroadcaster()` is the only way to add them.

## Connection Resilience

The `TrainEventReceiverService` automatically retries if the transport connection fails (e.g., RabbitMQ is unavailable at startup). It uses exponential backoff starting at 5 seconds, capping at 2 minutes. The service will not crash the host. It logs a warning and keeps retrying until the transport becomes available or the host shuts down.

The same holds for a connection the broker refuses, such as a wrong user or password (`ACCESS_REFUSED`): the host starts, runs its trains, and logs a warning, and no event crosses between processes. Nothing fails, so check the startup log for `RabbitMQ receiver started on exchange trax.lifecycle` on every host, and give every host the user the broker actually has.

## De-duplication

When a train runs locally on the hub (via a `run` mutation), the `GraphQLSubscriptionHook` fires directly and notifies subscribers. The same event is also published to the message bus by the broadcast lifecycle hook. `UseBroadcaster()` gives each host an instance id (a GUID, one per service provider), the broadcast hook and change sink stamp it on every message as `InstanceId`, and the `TrainEventReceiverService` **skips only messages carrying its own host's id**. This prevents double-notification without dropping anything another host published.

Because the id is per host rather than per application, replicas of one app behind a load balancer (which share an entry assembly, and so an `Executor`) each receive the others' events. A message from a publisher on a version without `InstanceId` has none, and is always delivered.

The `Executor` field is kept for display. It is stamped by the **broadcasting process** (via `Assembly.GetEntryAssembly()`), not copied from `metadata.Executor`, because metadata may be pre-created by a different process (e.g., the API pre-creates metadata for queued jobs that execute on a worker).

## Abstractions

The broadcaster system is built on four interfaces that allow alternative transport implementations:

```csharp
// Publishes lifecycle events to a message bus
public interface ITrainEventBroadcaster
{
    Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}

// Receives lifecycle events from a message bus
public interface ITrainEventReceiver : IAsyncDisposable
{
    Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    );
    Task StopAsync(CancellationToken ct);
}

// Handles received events (e.g., forwarding to GraphQL subscriptions)
public interface ITrainEventHandler
{
    Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
```

The `TrainLifecycleEventMessage` (namespace `Trax.Effect.Services.TrainEventBroadcaster`, like the three interfaces) is a serializable record containing:

| Field | Type | Description |
|-------|------|-------------|
| `MetadataId` | `long` | Database metadata row ID (`0` on a `DataChanged` signal) |
| `ExternalId` | `string` | External identifier for the execution (empty on a `DataChanged` signal) |
| `TrainName` | `string` | Canonical train name, the train interface's full name (empty on a `DataChanged` signal) |
| `TrainState` | `string` | Current state (serialized as string for transport) |
| `Timestamp` | `DateTime` | When the event occurred |
| `FailureJunction` | `string?` | Junction that failed (if applicable). Always null on a junction event. |
| `FailureReason` | `string?` | Failure message (if applicable) |
| `EventType` | `string` | See the event types below |
| `Executor` | `string?` | Assembly name of the process that broadcast the event, for display |
| `Output` | `string?` | The completed train's output as JSON, with `[TraxSensitive]` members masked. With [`SaveTrainParameters`](/docs/sdk-reference/configuration/save-train-parameters) it follows the stored copy: `null` for an output excluded by `SaveOutputs = false`, `ExcludeOutput` or `ShouldSaveOutputs`, and bounded by `MaxParameterBytes`. Without it, every completed train's output is serialized for the hooks, up to 1 MiB. An output over its ceiling is replaced by a `{"_truncated": true, ...}` placeholder. `null` on any event but a completion's `Completed` and `StateChanged` |
| `HostName` | `string?` | Machine name of the host that ran the train |
| `HostEnvironment` | `string?` | Environment name of the host that ran the train |
| `ChangeDomain` | `string?` | The changed domain on a `DataChanged` signal (`WorkQueue`, `DeadLetter`, `Manifest`, `ManifestGroup`, `SchedulerConfig`, `Execution`); `null` otherwise |
| `InstanceId` | `string?` | Id of the host that published the message, used for de-duplication (see above); `null` from a publisher that predates it |
| `FailureException` | `string?` | Type name of the exception a failed run recorded (`Metadata.FailureException`), such as `TrainException`; `null` otherwise, and from a publisher that predates it |

`Output`, `HostName`, `HostEnvironment`, `ChangeDomain`, `InstanceId` and `FailureException` are optional on the wire, so a message from an older publisher still deserializes.

`EventType` is one of:

| Event type | Published when |
|------------|----------------|
| `Started` | A run's row is saved as in progress |
| `Completed` | A run completes |
| `Failed` | A run fails |
| `Cancelled` | A run is cancelled |
| `StateChanged` | After each of the four above. Every transition is published twice, once under its own type and once as `StateChanged`, so a subscriber to the aggregate stream (the GraphQL `onTrainStateChanged` subscription) is fed on every host |
| `DataChanged` | A coalesced data-change signal, not a train event. Only `ChangeDomain`, `Timestamp`, `Executor` and `InstanceId` are set. A handler that only cares about trains ignores it (`TrainLifecycleEventMessage.DataChangedEventType`) |

## Transports

### RabbitMQ

```csharp
effects.UseBroadcaster(b => b.UseRabbitMq("amqp://guest:guest@localhost:5672"))
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `connectionString` | `string` | Yes | N/A | AMQP connection URI |
| `configure` | `Action<RabbitMqBroadcasterOptions>?` | No | `null` | Optional callback to customize options |

Options:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | `string` | N/A | AMQP connection URI |
| `ExchangeName` | `string` | `"trax.lifecycle"` | Fanout exchange name |
| `JunctionExchangeName` | `string?` | `ExchangeName` + `".junctions"` | Fanout exchange [junction events](/docs/effect/junction-events) are published to. Must differ from `ExchangeName`, or `UseRabbitMq` throws `ArgumentException`. |
| `PrefetchCount` | `ushort` | `64` | How many received events the receiver may hold unacknowledged at once. The broker holds the rest until the handlers acknowledge one, so a slow handler leaves events queued on the broker rather than in the receiving process. `0` (no limit) is refused: `UseRabbitMq` throws `ArgumentException`, and a directly constructed receiver throws `InvalidOperationException` from `StartAsync`. |

The RabbitMQ transport uses a **fanout exchange** so all connected hub instances receive every event. Each receiver creates its own exclusive, auto-delete queue. Every process bound to an exchange receives every event published to it, whichever application published it, so each application on a shared broker needs its own `ExchangeName` (or its own vhost).

Publishing never waits on the broker. A lifecycle hook is awaited inside the train, so the broadcaster writes each event to a bounded queue (1024 events) and returns; one background sender publishes the queue in order.

Each publish waits for the broker's publisher confirm, for at most 5 seconds. An event the broker has not confirmed is sent again, so a receiver can occasionally see the same event twice, but an event lost on a connection that died while still reporting open is not counted as sent. An attempt that times out discards the connection as well as the channel. The exchange is declared on every channel the sender opens, so one deleted, or lost with a broker restart, is recreated.

While the broker is unreachable the sender retries the event it holds with a growing delay (1 second, doubling to 30), each connection attempt bounded at 5 seconds, and further events wait in the queue. An event the broker refuses, by closing the channel on it with a channel-level error (`403`, `404`, `405`, `406`) or by rejecting the publish, is tried 3 times, then dropped and logged as an `Error`, so a refusal that does not clear, such as an exchange of the same name declared with another type, does not hold up the events behind it.

When the queue is full, junction events give way first, then non-terminal events. A new junction event is dropped. A new `Started`, `StateChanged` or `DataChanged` event takes the place of the oldest queued junction event, or is dropped when none is queued. A new `Completed`, `Failed` or `Cancelled` event takes the place of the oldest queued junction or non-terminal event, and is dropped itself only when every queued event is terminal. So a run whose `Started` reached subscribers keeps its outcome, although a subscriber can see an outcome without the `Started` before it. The first drop logs a `Warning`, followed by a second one giving the count when the queue drains.

A connection the broker closed is disposed before it is replaced. On shutdown the broadcaster waits up to 5 seconds for queued events to be sent, and does not wait at all while it is failing to reach the broker. Delivery is best effort: a consumer that must not miss an event reads it from the store.

Stopping the receiver tolerates a connection the broker has already closed, for example when the broker restarts or another host sharing it shuts down first. When `TrainEventReceiverService` retries a receiver that failed to start, each new start closes and disposes the connection the previous attempt opened, and a start that fails partway releases what it opened.

```csharp
effects.UseBroadcaster(b =>
    b.UseRabbitMq("amqp://localhost", opts =>
        opts.ExchangeName = "my-app.lifecycle"
    )
)
```

#### Junction events and rollout order

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

No upgrade order is required. Upgrade the hosts that should show steps (they bind the junction
exchange once they have a junction event handler), then turn on `AddJunctionEvents` on the
workers; until a host binds it, the steps a worker publishes go nowhere. Rolling a worker back
stops its steps and nothing else.

A step and its run's own events travel through different exchanges, so a subscriber can see a
run's `Completed` before its last step. Each step carries its position and timestamps; order by
those.

### SignalR

```csharp
effects.UseBroadcaster(b => b.UseSignalRHub())
```

SignalR is a **sink**, not a transport. It pushes events to connected browser or JS clients in real time. Compose it alongside a transport like RabbitMQ for cross-process delivery, or use it on its own when producer and consumer run in the same process.

See [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) for filtering and projection options, and [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) for the endpoint mapping that browsers connect to.

## Example: Distributed Workers

Both the hub (API + scheduler) and worker processes call `UseBroadcaster()` with the same RabbitMQ connection:

**Hub (Program.cs):**

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler => scheduler /* ... */)
);

// AddTraxGraphQL() auto-detects the broadcaster and registers
// GraphQLTrainEventHandler to forward remote events to subscriptions
builder.Services.AddTraxGraphQL();
```

**Worker (Program.cs):**

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxWorker(opts => { opts.WorkerCount = 4; });
```

## GraphQL Integration

When `AddTraxGraphQL()` detects that `ITrainEventReceiver` is registered (via `UseBroadcaster()`), it automatically registers `GraphQLTrainEventHandler` as an `ITrainEventHandler`. This handler maps received `TrainLifecycleEventMessage` records to `TrainLifecycleEvent` DTOs and sends them to HotChocolate's `ITopicEventSender`, making them available to all connected WebSocket subscribers.

No additional configuration is needed. Just call `UseBroadcaster()` in your effects and `AddTraxGraphQL()` as usual.

## Architecture

```
Worker Process                          Hub Process
─────────────                          ────────────
Train.Run()                            GraphQL Subscription Clients
  → LifecycleHookRunner                  ↑
    → BroadcastLifecycleHook             TrainEventReceiverService
      → ITrainEventBroadcaster             → GraphQLTrainEventHandler
        → RabbitMQ Exchange ─────────→       → ITopicEventSender
                                               → WebSocket delivery
```

The database remains the **single source of truth** for all train data. The broadcaster carries lifecycle events, and a completion (`Completed`, and the `StateChanged` after it) carries the train's output (see `Output` above), so it reaches every consumer of the exchange and every SignalR and GraphQL subscriber the events are forwarded to. Keep an output you do not want broadcast out with `ExcludeOutput` or `[TraxSensitive]`. All metadata, logs, manifests, and train state are always persisted to and queried from PostgreSQL.

## Implementing a Custom Transport

To implement a transport other than RabbitMQ:

1. Implement `ITrainEventBroadcaster` and `ITrainEventReceiver`
2. Create an extension method on `BroadcasterBuilder` that registers both:

```csharp
public static BroadcasterBuilder UseMyTransport(
    this BroadcasterBuilder builder,
    string connectionString
)
{
    builder.ServiceCollection.AddSingleton<ITrainEventBroadcaster>(
        new MyTransportBroadcaster(connectionString)
    );
    builder.ServiceCollection.AddSingleton<ITrainEventReceiver>(
        new MyTransportReceiver(connectionString)
    );
    return builder;
}
```

On a host that calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events),
the broadcaster is also handed every junction event, a message whose `Junction` is set
(`TrainLifecycleEventMessage.IsJunctionEvent`), on the run's path. Queue rather than wait, and
consider routing steps apart from train events, so a receiver that predates junction events never
sees one.

## Packages

```
dotnet add package Trax.Effect                        # Abstractions
dotnet add package Trax.Effect.Broadcaster.RabbitMQ    # RabbitMQ transport
dotnet add package Trax.Effect.Broadcaster.SignalR     # SignalR sink for browsers
```
