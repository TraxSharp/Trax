---
title: UseSignalRHub
description: "Reference for UseSignalRHub, the broadcaster sink that pushes train lifecycle events to SignalR clients: filters, projections, junction events and delivery."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 52
package: Trax.Effect.Broadcaster.SignalR
namespace:
  - Trax.Effect.Broadcaster.SignalR.Extensions
  - Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions
  - Trax.Effect.Broadcaster.SignalR.Models
source:
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR/Extensions/SignalRBroadcasterExtensions.cs
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR/Configuration/SignalRSinkOptions
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR/Models
---

# UseSignalRHub

Adds a SignalR sink to the broadcaster, so clients connected to the Trax hub receive train lifecycle events as they
happen. It runs beside a transport such as [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq), or on
its own when the runs and the clients share a process.

## Signature

```csharp fragment
public static BroadcasterBuilder UseSignalRHub(
    this BroadcasterBuilder builder,
    Action<SignalRSinkOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<SignalRSinkOptions>?` | Filters, projections and the delivery queue |

### Options

| Method | Parameters | Description |
|--------|------------|-------------|
| `OnlyForEvents` | `params string[] eventTypes` | Restrict to listed event types (`Started`, `Completed`, `Failed`, `Cancelled`, `StateChanged`). Multiple calls accumulate. Without a call, every event type is allowed. |
| `OnlyForTrains<T1>()` ... `<T1, T2, T3>()` | type parameters | Restrict to listed train interface types. Stores `typeof(T).FullName` (the canonical identifier Trax puts on the wire), not the short name. |
| `OnlyForTrains` | `params Type[]` | Same as the generic overloads. Throws if any type is not an interface. |
| `WithProjection<TClient>` | `Func<TrainLifecycleEventMessage, TClient>` | Replace the default `TraxClientEvent` projection. Last call wins. |
| `WithJunctionEvents` | none | Also send [junction events](#junction-events) through the `"JunctionEvent"` client method, without answers. Off by default. |
| `WithJunctionAnswers` | none | As `WithJunctionEvents`, with each question's answer and confidence and the names and question keys of steps on a track |
| `WithJunctionProjection<TClient>` | `Func<TrainLifecycleEventMessage, TClient>` | As `WithJunctionEvents`, with junction events in a shape of your own. Used instead of the default payload whether `WithJunctionAnswers` is called before or after it. `WithProjection` shapes train events only. |
| `WithDeliveryQueueCapacity` | `int capacity` | How many events may wait for delivery to clients. Default `SignalRSinkOptions.DefaultDeliveryQueueCapacity` (1024). Throws `ArgumentOutOfRangeException` below 1. See [Delivery](#delivery). |

## Returns

`BroadcasterBuilder`, for chaining.

## Behavior

The sink is registered as both an `ITrainLifecycleHook` (so local-process events flow directly) and an `ITrainEventHandler` (so events arriving over a transport like RabbitMQ also reach connected clients). A single singleton dispatcher backs both paths. The host maps the hub with [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub),
which needs `AddSignalR()` and an authorization posture. Each run is handed a forwarder to the dispatcher rather than
the dispatcher itself, so the end of a run, which disposes its hooks, never stops the sink. Data-change signals
(`DataChanged`) arriving over the transport are not sent to clients.

### Default projection

When `WithProjection` is not called, every event that passes the filters is projected to a `TraxClientEvent`:

| Field | Type | Source |
|-------|------|--------|
| `MetadataId` | `long` | `TrainLifecycleEventMessage.MetadataId` |
| `ExternalId` | `string` | `TrainLifecycleEventMessage.ExternalId` |
| `TrainName` | `string` | `TrainLifecycleEventMessage.TrainName` (interface FullName) |
| `EventType` | `string` | `Started` \| `Completed` \| `Failed` \| `Cancelled` \| `StateChanged` |
| `Timestamp` | `DateTime` | `TrainLifecycleEventMessage.Timestamp` |
| `FailureReason` | `string?` | Always `null`, and left off the wire |

The failure reason is the text the failing code put in its exception message, and every client the hub admits receives every train's events, so the default projection does not send it. The `Executor`, `TrainState`, `Output`, `HostName`, `HostEnvironment` and `FailureException` fields on the original message are dropped too. `WithProjection` sends any of them. The reasoning is recorded in Trax.Effect's ADR 0017.

#### Sending a failure reason

`FailureException` carries the short type name of the exception the run recorded. Sending the reason
only for a `TrainException`, the type a train author throws for a message meant to be read, is the
rule GraphQL subscriptions apply to clients outside the operations view; the second example below does it. A
subclass of `TrainException` reports its own type name. The
[SignalR Broadcaster sample](/docs/samples/signalr-broadcaster) uses this projection and tests both cases.

### Junction events

On a host that calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events),
the sink can also send each step of a run. Without one of the three options below it sends none.

A junction event goes to the same clients as its train's events and passes the same filters:
`OnlyForTrains` applies to it, and so does `OnlyForEvents` when it was called, in which case the
junction event types to send (`JunctionStarted`, `JunctionCompleted`, `JunctionFailed`,
`JunctionCancelled`, `Decided`, `DecisionRefused`, `Routed`) must be listed too.

By default each step is projected to a `TraxJunctionClientEvent` and sent through the
`"JunctionEvent"` client method:

| Field | Type | Source |
|-------|------|--------|
| `MetadataId`, `ExternalId`, `TrainName`, `EventType`, `Timestamp` | | As on `TraxClientEvent` |
| `Position` | `int` | Where the step falls in the run, from 0 |
| `Kind` | `string` | `Junction`, `Choice`, `Score`, `YesNo` or `Route` |
| `Name` | `string` | The junction's class name, or the question's key. `(withheld)` for any step on a track (a junction, a question or a route) unless `WithJunctionAnswers()` was called, and always for one the run withheld after a `[TraxSensitive]` route. |
| `State` | `string` | `InProgress`, `Completed`, `Failed` or `Cancelled` |
| `StartedAt`, `EndedAt`, `DurationMs` | | When the step started and ended, and its duration |
| `FailureClass`, `FailureException` | `string?` | How a failed junction's failure is classified, and its exception's type name |
| `QuestionKey` | `string?` | The question's key, for a question or a track. Null, and left off the wire, whenever `Name` is withheld. |
| `Answer`, `Confidence` | | Null, and left off the wire, unless `WithJunctionAnswers()` was called |
| `Replayed` | `bool` | Whether the answer was replayed from an earlier run |
| `AnswerWithheld` | `bool` | True for a question about a `[TraxSensitive]` type, and for a question or route the run withheld after one |
| `NameWithheld` | `bool` | True when `Name` is withheld |
| `TrackPosition` | `int?` | For any step, the position of the route whose track it is on; left off the wire when null |
| `NodeId` | `string?` | The id of the declared node the step ran for. Null, and left off the wire, whenever `Name` is withheld. |
| `BranchPath` | `string?` | The `Parallel` branch the step ran in. Left off the wire outside any branch and whenever `Name` is withheld. |

Every client the hub admits receives every train's events, so the default payload carries no
answer or confidence, and no name or question key for a step on a track, since which steps ran
names the track taken. Each step's kind, position, state, timing and failure class are still sent.
`WithJunctionAnswers()` adds the rest; answers to questions about a
[`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) type stay
withheld. No payload carries an input, output or failure message. When the delivery queue is full,
an incoming train event takes the place of the oldest queued junction event before it is dropped
itself, so steps are given up first.

### Delivery

The lifecycle hook and the event handler do not wait for clients. They apply the filters and write the event to a bounded queue, then return. One background sender takes events off the queue in the order they were raised, applies the projection, and sends each to `Clients.All`. A train's `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled` and `OnStateChanged` therefore take the same time whether the connected clients are fast, slow or stalled.

Delivery is best effort. `Clients.All` waits for every connection, so a client that cannot keep up slows the sender for everyone until [`MapTraxTrainEventHub`'s send timeout](/docs/sdk-reference/configuration/map-trax-train-event-hub#send-timeout) disconnects it. If clients fall behind by more than the queue's capacity, further events are dropped rather than holding up trains. The first drop logs a `Warning` naming the capacity, and when the queue next empties a second `Warning` gives how many were dropped.

The dispatcher is also a hosted service. When the host stops, it stops accepting events and waits for the queued ones to be sent, within the host's shutdown timeout; whatever is left when that timeout runs out is abandoned and logged. The reasoning is recorded in Trax.Effect's ADR 0012.

### Error handling

If the hub send fails for any reason (e.g. a client disconnects mid-send, a transient transport error), the background sender logs at `Error` level and carries on with the next event. A slow or broken client never throws out of the lifecycle hook or event handler, and never holds them up.

## Examples

```csharp compile
builder.Services.AddSignalR();

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b
            .UseRabbitMq(rabbitMqConnectionString)
            .UseSignalRHub(opts => opts
                .OnlyForEvents("Completed", "Failed")
                .OnlyForTrains<IShipOrderTrain, IRunAuditTrain>()))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireAuthorization("TraxEvents"));
```

Clients the `TraxEvents` policy admits receive a `TrainEvent` for every `Completed` or `Failed` event of the two
trains, including runs on other processes, which arrive over RabbitMQ.

A projection that sends the failure reason only for a `TrainException`:

```csharp compile
using Trax.Core.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseSignalRHub(opts => opts.WithProjection(LiveTrainEvent.From)))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);

public sealed record LiveTrainEvent(
    string ExternalId, string TrainName, string EventType, DateTime Timestamp, string? FailureReason)
{
    public static LiveTrainEvent From(TrainLifecycleEventMessage message) =>
        new(message.ExternalId, message.TrainName, message.EventType, message.Timestamp,
            message.EventType != "Failed" ? null
            : message.FailureException == nameof(TrainException) ? message.FailureReason
            : "The run failed.");
}
```

Junction events for one train, with a larger delivery queue:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionEvents()
        .UseBroadcaster(b => b.UseSignalRHub(opts => opts
            .OnlyForTrains<IShipOrderTrain>()
            .WithJunctionEvents()
            .WithDeliveryQueueCapacity(4096)))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Push train events to browsers](/docs/effect/push-train-events-to-browsers), [How event broadcasting works](/docs/effect/how-event-broadcasting-works)
- [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub), [ITraxTrainEventClient](/docs/sdk-reference/configuration/i-trax-train-event-client), [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster)
