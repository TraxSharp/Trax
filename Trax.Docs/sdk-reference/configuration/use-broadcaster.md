---
layout: default
title: UseBroadcaster
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
| `BroadcastLifecycleHook` | Lifecycle hook that publishes events to `ITrainEventBroadcaster` |
| `TrainEventReceiverService` | `BackgroundService` that consumes events from `ITrainEventReceiver` and dispatches to `ITrainEventHandler` instances |

The transport-specific `ITrainEventBroadcaster` and `ITrainEventReceiver` are registered by the callback (e.g., `UseRabbitMq()`).

## Connection Resilience

The `TrainEventReceiverService` automatically retries if the transport connection fails (e.g., RabbitMQ is unavailable at startup). It uses exponential backoff starting at 5 seconds, capping at 2 minutes. The service will not crash the host. It logs a warning and keeps retrying until the transport becomes available or the host shuts down.

## De-duplication

When a train runs locally on the hub (via a `run` mutation), the `GraphQLSubscriptionHook` fires directly and notifies subscribers. The same event is also published to the message bus by `BroadcastLifecycleHook`. The `TrainEventReceiverService` detects this by comparing the event's `Executor` field against the local process name and **skips events that originated locally**. This prevents double-notification.

The `Executor` field is always stamped by the **broadcasting process** (via `Assembly.GetEntryAssembly()`), not copied from `metadata.Executor`. This is important because metadata may be pre-created by a different process (e.g., the API pre-creates metadata for queued jobs that execute on a worker). If the hook used `metadata.Executor`, the hub would incorrectly discard worker events as "local."

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

The `TrainLifecycleEventMessage` is a serializable record containing:

| Field | Type | Description |
|-------|------|-------------|
| `MetadataId` | `long` | Database metadata row ID |
| `ExternalId` | `string` | External identifier for the execution |
| `TrainName` | `string` | Fully-qualified train class name |
| `TrainState` | `string` | Current state (serialized as string for transport) |
| `Timestamp` | `DateTime` | When the event occurred |
| `FailureJunction` | `string?` | Junction that failed (if applicable) |
| `FailureReason` | `string?` | Failure message (if applicable) |
| `EventType` | `string` | One of: `Started`, `Completed`, `Failed`, `Cancelled` |
| `Executor` | `string?` | Assembly name of the process that broadcast the event |

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

The RabbitMQ transport uses a **fanout exchange** so all connected hub instances receive every event. Each receiver creates its own exclusive, auto-delete queue.

```csharp
effects.UseBroadcaster(b =>
    b.UseRabbitMq("amqp://localhost", opts =>
        opts.ExchangeName = "my-app.lifecycle"
    )
)
```

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

The database remains the **single source of truth** for all train data. The broadcaster only carries lightweight lifecycle event notifications. All metadata, logs, manifests, and train state are always persisted to and queried from PostgreSQL.

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

## Packages

```
dotnet add package Trax.Effect                        # Abstractions
dotnet add package Trax.Effect.Broadcaster.RabbitMQ    # RabbitMQ transport
```
