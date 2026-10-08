---
layout: default
title: AddJunctionEvents
description: Reference for AddJunctionEvents, which publishes each step of a run live and records it in trax.junction_run, with IJunctionEventHandler and ForRun.
parent: Configuration
grand_parent: SDK Reference
nav_order: 24
---

# AddJunctionEvents

Publishes each step of every run (each junction as it starts and ends, each question a routing
step asks and the answer the run acts on, each track it takes) and records it in
`trax.junction_run`. Off unless this is called. See [Junction Events](/docs/effect/junction-events).

## Signature

```csharp
public static TraxEffectBuilderWithData AddJunctionEvents(
    this TraxEffectBuilderWithData configurationBuilder
)
```

Defined on `TraxEffectBuilderWithData`, so it comes after a data provider. Called before one, it
is a compile error: `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddJunctionEvents().`

## Returns

`TraxEffectBuilderWithData`, for continued chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDecisionRecording()
        .AddJunctionEvents()
        .UseBroadcaster(b => b
            .UseRabbitMq(rabbitMqUrl)
            .UseSignalRHub(opts => opts.WithJunctionEvents()))
    )
);
```

## What it registers

| Service | Lifetime | Role |
|---|---|---|
| Junction event publisher | Singleton | Publishes each step to the host's `IJunctionEventHandler`s and to the broadcaster's transport, when there is one |
| Junction run writer | Singleton, and a hosted service | Stores steps in `trax.junction_run` from a background queue of 4,096; stopping the host drains it |
| An `IDecisionObserver` | Singleton | Turns each decision and routing into a step. Told after required observers such as decision recording's ([Other decision observers](/docs/effect/decisions#other-decision-observers)). Withholding a track depends on it hearing every routing, so an `IDecisionObserver` registered after `AddTrax`, which would replace it, refuses the host's start and every run. |

Calling `AddJunctionEvents()` more than once registers these once.

## Event types

Each step is a `TrainLifecycleEventMessage` whose `EventType` is one of these constants, with the
step in `Junction` (a `JunctionEventPayload`). `TrainLifecycleEventMessage.IsJunctionEvent(eventType)`
says whether an event type is one of them.

| Constant | Value | Published when |
|---|---|---|
| `JunctionStartedEventType` | `"JunctionStarted"` | A junction starts |
| `JunctionCompletedEventType` | `"JunctionCompleted"` | A junction returns a result |
| `JunctionFailedEventType` | `"JunctionFailed"` | A junction fails |
| `JunctionCancelledEventType` | `"JunctionCancelled"` | A junction is stopped by a cancellation the run was asked for |
| `DecidedEventType` | `"Decided"` | A routing step's question is answered and the run acts on the answer |
| `DecisionRefusedEventType` | `"DecisionRefused"` | A decider's answer the run will not act on; the routing step fails |
| `RoutedEventType` | `"Routed"` | A routing step sends the run down a track |

## JunctionEventPayload

```csharp
// Trax.Effect.Services.TrainEventBroadcaster
public sealed record JunctionEventPayload(
    int Position,
    JunctionRunKind Kind,
    string Name,
    JunctionRunState State,
    DateTime StartedAt,
    DateTime? EndedAt = default,
    double? DurationMs = default,
    FailureClass? FailureClass = default,
    string? FailureException = null,
    string? QuestionKey = null,
    string? Answer = null,
    double? Confidence = default,
    bool Replayed = false,
    string? Decider = null,
    bool AnswerWithheld = false,
    int? Attempt = default,
    bool NameWithheld = false,
    int? TrackPosition = default
)
{
    public const string WithheldName = "(withheld)";

    public string? NodeId { get; init; }
}
```

| Field | Description |
|---|---|
| `Position` | Where the step falls in the run, from 0. A junction's start and end share it. |
| `Kind` | `Junction`, `Choice` (`Decide`, `Switch`), `Score` (`Scale`), `YesNo` (`Gate`) or `Route` |
| `Name` | The junction's class name without its namespace, or a question's key; `WithheldName` when `NameWithheld` is set |
| `State` | `InProgress`, `Completed`, `Failed` or `Cancelled` |
| `StartedAt` | When the junction started, or when the question was answered or the track taken (UTC) |
| `EndedAt`, `DurationMs` | When the junction returned and how long it took; null for a start. A question or route has `EndedAt` equal to `StartedAt` and `DurationMs` 0. |
| `FailureClass` | How a failed junction's failure is classified; null unless it failed |
| `FailureException` | The type name of the exception a junction failed or was cancelled with, never its message |
| `QuestionKey` | The question's key, for a question or a track; null when `NameWithheld` is set |
| `Answer` | The option, score or probability of yes the run acted on; null for a junction, a refused answer and a withheld one |
| `Confidence` | The decider's confidence, for a choice or score; null when withheld |
| `Replayed` | True when the answer came from an earlier run. Set on a question's step only; a `Route` step carries false. |
| `Decider` | The full name of the decider's type; null when `NameWithheld` is set. Not stored. |
| `AnswerWithheld` | True when the question is about a type marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type), and for a question or route after a withheld route |
| `Attempt` | Which attempt of its manifest the run is, or null for a run with no manifest |
| `NameWithheld` | True for every step (a junction, a question or a route) after a route whose answer is withheld, because which steps ran would give the track away. A question's or route's key, answer, confidence and decider are left out with the name. |
| `TrackPosition` | For any step, the position of the latest route the run took before it, or null before any route. Every step after a route counts as on its track. A consumer that does not show a reader answers should not show these steps' names or question keys either. |
| `NodeId` | The id of the declared node the step ran for, as [`ChainGraph.From`](/docs/effect/junction-events#placing-a-step-on-the-trains-graph) draws it, such as `Switch<Source>#0/Papers/FetchPapers#0`. Null when `NameWithheld` is set, because the id names the track and the step. |

It never carries a junction's input or output, the train's input or output, a failure's message,
or the state, instructions or criteria of a question. On a withheld track the number, kinds,
positions and timing of steps, a failed junction's exception type and failure class, the run's own
failure junction and any train started on the track stay visible; see
[Withholding an answer](/docs/effect/junction-events#withholding-an-answer).

## IJunctionEventHandler

```csharp
// Trax.Effect.Services.TrainEventBroadcaster
public interface IJunctionEventHandler
{
    Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct);
}
```

Register implementations in the container. Each message has a junction event type and a non-null
`Junction`. Junction events reach only these handlers, never an `ITrainEventHandler`.

- On the host that runs the train, a handler is called on the run's path, resolved from the run's
  scope, right after the step is published. The next junction waits for it, so it must return
  quickly and queue anything slow.
- On other hosts it is called by `TrainEventReceiverService` for steps arriving over the transport,
  resolved from a fresh scope per message. A step this host published is not delivered to it twice.
- Whatever it throws is logged and does not reach the run or the other handlers.

## JunctionRun and ForRun

Each stored step is a `JunctionRun` on `IDataContext.JunctionRuns`, with the payload's fields
except `Decider`, plus `Id` and `MetadataId`. Read one run's steps, ordered by position, with
`ForRun`:

```csharp
// Trax.Effect.Data.JunctionEvents
public static IOrderedQueryable<JunctionRun> ForRun(
    this IQueryable<JunctionRun> runs,
    long metadataId
)
```

```csharp
var steps = await dataContext.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync(ct);
```

The API's `operations.junctionRuns` and the dashboard's timeline read through it too.

## Remarks

- Nothing it does can fail a run or change a junction's result: a failure to store, broadcast or
  hand out a step is logged and swallowed.
- A step is stored by a background writer, in order and in batches, through a data context of its
  own, so the run never waits on the database. A full queue drops a step, counted and logged. A
  junction whose end was dropped stays `InProgress` after its run has ended, so read a row's state
  together with its run's.
- A run's rows are deleted with its metadata row, by the foreign key's cascade.
- Only `EffectJunction`s are steps. A junction skipped because an earlier one failed is not a step,
  and a run that is not saved (no metadata row) publishes none.
- A custom `ITrainEventBroadcaster` is handed every junction event too, on the run's path; it should
  queue rather than wait, and may route steps apart from train events.
- A run of a manifest carries its attempt: 1 plus the manifest's failed runs since its last
  completed or cancelled one, read once when the run begins from the manifest's 1000 most recent
  runs (a longer streak is reported as 1001) and waited on for at most a second. A failure to read it
  leaves it out.
- Junction events carry `FailureJunction` as null; the run's own `Failed` event names the junction.
- With RabbitMQ, steps go to their own exchange, and after a failure there are dropped untried for a
  backoff of one second doubling to a minute; see
  [UseBroadcaster: RabbitMQ](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq). With
  SignalR, they reach clients only after
  [`WithJunctionEvents()`](/docs/sdk-reference/configuration/use-signalr-hub#junction-events).

## Package

```
dotnet add package Trax.Effect.Data
```

The extension is in namespace `Trax.Effect.Data.Extensions`.
