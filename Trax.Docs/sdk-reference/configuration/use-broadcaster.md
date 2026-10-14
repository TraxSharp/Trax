---
title: UseBroadcaster
description: Reference for UseBroadcaster, which carries train lifecycle events and data-change signals between processes over a transport such as RabbitMQ.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 50
package: Trax.Effect
namespace:
  - Trax.Effect.Extensions
  - Trax.Effect.Configuration.BroadcasterBuilder
  - Trax.Effect.Services.TrainEventBroadcaster
source:
  - Trax.Effect/src/Trax.Effect/Extensions/BroadcasterExtensions.cs
  - Trax.Effect/src/Trax.Effect/Configuration/BroadcasterBuilder/BroadcasterBuilder.cs
  - Trax.Effect/src/Trax.Effect/Services/TrainEventBroadcaster
---

# UseBroadcaster

Publishes every run's lifecycle events (started, completed, failed, cancelled) and the coalesced data-change signals
to a transport, and receives the events other processes publish. With it, [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions)
and SignalR clients see runs executed in any process; without it, only runs in their own process.

## Signature

```csharp fragment
public static TBuilder UseBroadcaster<TBuilder>(
    this TBuilder builder,
    Action<BroadcasterBuilder> configure
)
    where TBuilder : TraxEffectBuilder
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<BroadcasterBuilder>` | Selects the transport and sinks: [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq), [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) |

`BroadcasterBuilder` has `ServiceCollection` and `EffectRegistry`, for the transport and sink methods that extend it.

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

### Registered services

| Component | Description |
|-----------|-------------|
| Broadcast lifecycle hook | Internal, non-toggleable lifecycle hook that publishes events to `ITrainEventBroadcaster` |
| Broadcast change sink | Internal sink that forwards coalesced data-change signals (`onDataChanged`) to other processes over the same transport |
| `TrainEventReceiverService` | `BackgroundService` that consumes events from `ITrainEventReceiver` and dispatches train events to `ITrainEventHandler` instances, and [junction events](/docs/effect/junction-events) to `IJunctionEventHandler` instances only |

The transport-specific `ITrainEventBroadcaster` and `ITrainEventReceiver` are registered by the callback (e.g., `UseRabbitMq()`). The hook and the sink are internal types: `UseBroadcaster()` is the only way to add them.

### Connection resilience

The `TrainEventReceiverService` automatically retries if the transport connection fails (e.g., RabbitMQ is unavailable at startup). It uses exponential backoff starting at 5 seconds, capping at 2 minutes. The service will not crash the host. It logs a warning and keeps retrying until the transport becomes available or the host shuts down.

The same holds for a connection the broker refuses, such as a wrong user or password (`ACCESS_REFUSED`): the host starts, runs its trains, and logs a warning, and no event crosses between processes. A receiver that started logs `RabbitMQ receiver started on exchange trax.lifecycle`.

### De-duplication

When a train runs locally on the hub (via a `run` mutation), the `GraphQLSubscriptionHook` fires directly and notifies subscribers. The same event is also published to the message bus by the broadcast lifecycle hook. `UseBroadcaster()` gives each host an instance id (a GUID, one per service provider), the broadcast hook and change sink stamp it on every message as `InstanceId`, and the `TrainEventReceiverService` **skips only messages carrying its own host's id**. This prevents double-notification without dropping anything another host published.

Because the id is per host rather than per application, replicas of one app behind a load balancer (which share an entry assembly, and so an `Executor`) each receive the others' events. A message from a publisher on a version without `InstanceId` has none, and is always delivered.

The `Executor` field is kept for display. It is stamped by the **broadcasting process** (via `Assembly.GetEntryAssembly()`), not copied from `metadata.Executor`, because metadata may be pre-created by a different process (e.g., the API pre-creates metadata for queued jobs that execute on a worker).

### Abstractions

A transport implements `ITrainEventBroadcaster` and `ITrainEventReceiver`; handlers implement `ITrainEventHandler` (and `IJunctionEventHandler` for [junction events](/docs/sdk-reference/configuration/add-junction-events#ijunctioneventhandler)).

```csharp fragment
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

#### Event types

`EventType` is one of:

| Event type | Published when |
|------------|----------------|
| `Started` | A run's row is saved as in progress |
| `Completed` | A run completes |
| `Failed` | A run fails |
| `Cancelled` | A run is cancelled |
| `StateChanged` | After each of the four above. Every transition is published twice, once under its own type and once as `StateChanged`, so a subscriber to the aggregate stream (the GraphQL `onTrainStateChanged` subscription) is fed on every host |
| `DataChanged` | A coalesced data-change signal, not a train event. Only `ChangeDomain`, `Timestamp`, `Executor` and `InstanceId` are set. A handler that only cares about trains ignores it (`TrainLifecycleEventMessage.DataChangedEventType`) |

### RabbitMQ

`UseRabbitMq` registers the shipped transport. Its options, delivery and junction exchange are on
[UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq).

### SignalR

`UseSignalRHub` adds a sink, not a transport: it pushes events to connected browser and .NET clients. See
[UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) and
[MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub).

### GraphQL integration

When `AddTraxGraphQL()` detects that `ITrainEventReceiver` is registered (via `UseBroadcaster()`), it automatically registers `GraphQLTrainEventHandler` as an `ITrainEventHandler`. This handler maps received `TrainLifecycleEventMessage` records to `TrainLifecycleEvent` DTOs and sends them to HotChocolate's `ITopicEventSender`, making them available to all connected WebSocket subscribers.

### Implementing a custom transport

A transport is an extension method on `BroadcasterBuilder` that registers an `ITrainEventBroadcaster` and an
`ITrainEventReceiver`. On a host that calls `AddJunctionEvents()`, the broadcaster is handed every junction event too,
on the run's path. See [Implement a broadcast transport](/docs/effect/implement-a-broadcast-transport).

### What crosses the wire

The database remains the **single source of truth** for all train data. The broadcaster carries lifecycle events, and a completion (`Completed`, and the `StateChanged` after it) carries the train's output (see `Output` above), so it reaches every consumer of the exchange and every SignalR and GraphQL subscriber the events are forwarded to. Keep an output you do not want broadcast out with `ExcludeOutput` or `[TraxSensitive]`. All metadata, logs, manifests, and train state are always persisted to and queried from PostgreSQL.


## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Broadcast events between processes](/docs/effect/broadcast-events-between-processes), [How event broadcasting works](/docs/effect/how-event-broadcasting-works)
- [Broadcaster Sinks](/docs/effect/broadcaster-sinks)
- [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq), [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub)
