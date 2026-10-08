---
layout: default
title: UseSignalRHub
description: "Reference for UseSignalRHub, the broadcaster sink that pushes train lifecycle events to SignalR clients: options, projections, delivery and error handling."
parent: Configuration
grand_parent: SDK Reference
nav_order: 13
---

# UseSignalRHub

Adds a SignalR sink to the broadcaster pipeline so connected browser, Blazor, or JS clients receive train lifecycle events in real time. Composes alongside cross-process transports like [`UseRabbitMq`](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq), or runs on its own when producer and consumer share a process.

The sink is registered as both an `ITrainLifecycleHook` (so local-process events flow directly) and an `ITrainEventHandler` (so events arriving over a transport like RabbitMQ also reach connected clients). A single singleton dispatcher backs both paths.

## Signature

```csharp
public static BroadcasterBuilder UseSignalRHub(
    this BroadcasterBuilder builder,
    Action<SignalRSinkOptions>? configure = null
)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `builder` | `BroadcasterBuilder` | Yes | The broadcaster builder (inside the `UseBroadcaster` callback) |
| `configure` | `Action<SignalRSinkOptions>?` | No | Filter or projection configuration |

## Options

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

## Default projection

When `WithProjection` is not called, every event that passes the filters is projected to a `TraxClientEvent`:

| Field | Type | Source |
|-------|------|--------|
| `MetadataId` | `long` | `TrainLifecycleEventMessage.MetadataId` |
| `ExternalId` | `string` | `TrainLifecycleEventMessage.ExternalId` |
| `TrainName` | `string` | `TrainLifecycleEventMessage.TrainName` (interface FullName) |
| `EventType` | `string` | `Started` \| `Completed` \| `Failed` \| `Cancelled` \| `StateChanged` |
| `Timestamp` | `DateTime` | `TrainLifecycleEventMessage.Timestamp` |
| `FailureReason` | `string?` | Always `null`, and left off the wire |

The failure reason is the text the failing code put in its exception message, and every client the hub admits receives every train's events, so the default projection does not send it. The `Executor`, `TrainState`, `Output`, `HostName`, `HostEnvironment` and `FailureException` fields on the original message are dropped too. Use `WithProjection` to send any of them, once you know they are fit for every subscriber. The reasoning is recorded in Trax.Effect's ADR 0017.

## Example

```csharp
builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connectionString)
            .UseBroadcaster(b => b
                .UseRabbitMq(rabbitMqUrl)
                .UseSignalRHub(opts => opts
                    .OnlyForEvents("Completed", "Failed")
                    .OnlyForTrains<ICheckGeocodeDriftTrain, IRunAuditTrain>()))));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireAuthorization("TraxEvents"));
```

Browsers that the `TraxEvents` policy admits connect to `/hubs/trax-events` and receive a `TrainEvent` callback for every `Completed` or `Failed` lifecycle event of the listed trains. The example wires RabbitMQ alongside, so events from remote worker processes also reach the browser.

## Custom projection

```csharp
b.UseSignalRHub(opts => opts.WithProjection(msg =>
    new {
        msg.ExternalId,
        msg.TrainName,
        msg.EventType,
        Output = msg.Output,             // ship the train's Output through to the browser
        msg.FailureReason                // only when every subscriber may read it
    }))
```

The projection is invoked once per matching event, after filtering. The hub serializes the result via SignalR's configured `IHubProtocol` (JSON by default), so a C# record arrives in a JavaScript client with camelCase property names.

### Sending a failure reason safely

`FailureException` carries the short type name of the exception the run recorded. Sending the reason
only for a `TrainException`, the type a train author throws for a message meant to be read, is the
rule GraphQL subscriptions apply to clients outside the operations view:

```csharp
using Trax.Core.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

public sealed record LiveTrainEvent(
    string ExternalId, string TrainName, string EventType, DateTime Timestamp, string? FailureReason)
{
    public static LiveTrainEvent From(TrainLifecycleEventMessage message) =>
        new(message.ExternalId, message.TrainName, message.EventType, message.Timestamp,
            message.EventType != "Failed" ? null
            : message.FailureException == nameof(TrainException) ? message.FailureReason
            : "The run failed.");
}

b.UseSignalRHub(opts => opts.WithProjection(LiveTrainEvent.From));
```

A subclass of `TrainException` reports its own type name; list it as well if you throw one. The
[SignalR Broadcaster sample](/docs/samples/signalr-broadcaster) uses this projection and tests both
cases.

## Junction events

On a host that calls [`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events),
the sink can also send each step of a run. Without one of the three options below it sends none.

```csharp
b.UseSignalRHub(opts => opts
    .OnlyForTrains<IUnderwriteLoanTrain>()
    .WithJunctionEvents())
```

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

Every client the hub admits receives every train's events, so the default payload carries no
answer or confidence, and no name or question key for a step on a track, since which steps ran
names the track taken. Each step's kind, position, state, timing and failure class are still sent.
`WithJunctionAnswers()` adds the rest; answers to questions about a
[`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) type stay
withheld. No payload carries an input, output or failure message. When the delivery queue is full,
an incoming train event takes the place of the oldest queued junction event before it is dropped
itself, so steps are given up first.

## Delivery

The lifecycle hook and the event handler do not wait for clients. They apply the filters and write the event to a bounded queue, then return. One background sender takes events off the queue in the order they were raised, applies the projection, and sends each to `Clients.All`. A train's `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled` and `OnStateChanged` therefore take the same time whether the connected clients are fast, slow or stalled.

Delivery is best effort. `Clients.All` waits for every connection, so a client that cannot keep up slows the sender for everyone until [`MapTraxTrainEventHub`'s send timeout](/docs/sdk-reference/configuration/map-trax-train-event-hub#send-timeout) disconnects it. If clients fall behind by more than the queue's capacity, further events are dropped rather than holding up trains. The first drop logs a `Warning` naming the capacity, and when the queue next empties a second `Warning` gives how many were dropped. A client that must not miss an event reads it from the store or from a durable transport, not from this sink.

The dispatcher is also a hosted service. When the host stops, it stops accepting events and waits for the queued ones to be sent, within the host's shutdown timeout; whatever is left when that timeout runs out is abandoned and logged. The reasoning is recorded in Trax.Effect's ADR 0012.

```csharp
b.UseSignalRHub(opts => opts.WithDeliveryQueueCapacity(4096))
```

## Error handling

If the hub send fails for any reason (e.g. a client disconnects mid-send, a transient transport error), the background sender logs at `Error` level and carries on with the next event. A slow or broken client never throws out of the lifecycle hook or event handler, and never holds them up.

## Local vs remote coverage

The same singleton dispatcher is registered twice in DI:

- As an `ITrainLifecycleHook`, so trains running in the same process fire it directly with no transport hop. Each run is handed a forwarder to the dispatcher rather than the dispatcher itself, so the end of a run, which disposes its hooks, never stops the sink; only the host's shutdown does.
- As an `ITrainEventHandler`, so events arriving via `TrainEventReceiverService` (the broadcaster's transport-side consumer) also flow through.

The `TrainEventReceiverService` skips events stamped with this host's own instance id, so the dispatcher does not double-fire when both paths exist on one host, while replicas of the same app still receive each other's events. Data-change signals (`DataChanged`) arrive over the same transport but are not train events, so the event handler path does not send them to clients.

## Prerequisites

- Call `builder.Services.AddSignalR()` on the host. Without it, [`MapTraxTrainEventHub`](/docs/sdk-reference/configuration/map-trax-train-event-hub) throws an `InvalidOperationException` at startup.
- Map the hub with an authorization posture (`RequireAuthorization`, `RequireRoles`, or an explicit `AllowAnonymous`). See [MapTraxTrainEventHub: Authorization](/docs/sdk-reference/configuration/map-trax-train-event-hub#authorization).

## Packages

```
dotnet add package Trax.Effect.Broadcaster.SignalR
```

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
