---
layout: default
title: Junction Events
description: "AddJunctionEvents: each step of a run published live and stored in trax.junction_run, what a step carries, SignalR, RabbitMQ, GraphQL and the dashboard."
parent: Effect
nav_order: 9
---

# Junction Events

A train's lifecycle events say that a run started, completed or failed. Junction events say what
happened in between: each junction starting and ending, each question a routing step asked and the
answer the run acted on, and each track it took. They are published live and stored in
`trax.junction_run`, so a running run can be followed step by step and a finished one read back as
a timeline.

They are off unless the host calls `AddJunctionEvents()`:

```csharp
using Trax.Effect.Data.Extensions;            // AddJunctionEvents (package Trax.Effect.Data)
using Trax.Effect.Data.Postgres.Extensions;   // UsePostgres (package Trax.Effect.Data.Postgres)

services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddJunctionEvents()));
```

Only an `EffectJunction<TIn, TOut>` (namespace `Trax.Effect.Services.EffectJunction`) is reported:
a train built from plain `Junction<TIn, TOut>` classes publishes its questions and routes, but none of
its junctions.

It comes after a data provider, and is a compile error before one. Calling it more than once
registers it once. Withholding a track depends on its decision observer hearing every routing, so
an `IDecisionObserver` registered after `AddTrax`, which would replace it, refuses the host's start
and every run; register your own before `AddTrax` (see
[Other decision observers](/docs/effect/decisions#other-decision-observers)). `Trax.Effect/docs/adr/0019` records why each of the rules below is the way it is.

## What a step is

| Kind | Step | Event types |
|---|---|---|
| `Junction` | An `EffectJunction` that ran | `JunctionStarted`, then one of `JunctionCompleted`, `JunctionFailed`, `JunctionCancelled` |
| `Choice` | A question answered by choosing an option (`Decide`, `Switch`) | `Decided`, or `DecisionRefused` when the run would not act on the answer |
| `Score` | A question answered by a level (`Scale`) | `Decided` or `DecisionRefused` |
| `YesNo` | A question answered by the probability of yes (`Gate`) | `Decided` or `DecisionRefused` |
| `Route` | The track a routing step sent the run down | `Routed` |

A question or a route is a single moment: its `StartedAt` is when the answer arrived or the track
was taken, its `EndedAt` the same, and its `DurationMs` 0. The time a decider took is the gap between
the previous step's end and the question's `StartedAt`; a replayed answer closes that gap to almost
nothing.

Each step has a `Position` from 0, in the order the run reached it. A junction's start and end
share one position, so ordering by position gives the run's timeline. Only `EffectJunction`s are
steps: a plain `Junction` in a service train runs no junction effects and reports nothing, and a
junction skipped because an earlier one failed is not a step. A run that is not saved (it has no
metadata row) publishes no steps.

Trax.Core reports a decision without naming the routing step that asked it, so a question is told
apart by its kind rather than by `Switch` or `Gate`.

## What a step carries, and never carries

Each event is a `TrainLifecycleEventMessage` with a junction event type and its step in `Junction`,
a `JunctionEventPayload`:

| Field | Holds |
|---|---|
| `Position`, `Kind`, `Name` | Where the step falls, what it is, and the junction's class name without its namespace, or a question's key. `(withheld)` when `NameWithheld` is set. |
| `State` | `InProgress`, `Completed`, `Failed` or `Cancelled` |
| `StartedAt`, `EndedAt`, `DurationMs` | UTC times, and the junction's duration once it ends |
| `FailureClass`, `FailureException` | How a failed junction's failure is [classified](/docs/core/trains-and-junctions#classifying-failures), and its exception's type name |
| `QuestionKey`, `Answer`, `Confidence` | For a question or track: its [key](/docs/core/decisions#question-keys), the option, score or probability the run acted on, and the decider's confidence. Null on a withheld track. |
| `Replayed` | True when the answer was [replayed](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions) from an earlier run. Set on the question's step only: a `Route` step carries false even when the decision it routes on was replayed. False on a retry that asked afresh, whatever the reason; why is in `trax.decision` (see [Building a train that recovers](/docs/effect/decisions#building-a-train-that-recovers)). |
| `Decider` | The decider's type name. Live events only, and null on a withheld track. |
| `AnswerWithheld` | True when the question is about a `[TraxSensitive]` type, and for every question or route on a withheld track |
| `NameWithheld` | True for every step (a junction, a question or a route) after a route whose answer is withheld; its `Name` is `(withheld)` (`JunctionEventPayload.WithheldName`) |
| `TrackPosition` | For any step, the position of the latest route the run took before it, or null before any route. `Decided`, `DecisionRefused` and `Routed` carry it as junctions do. |
| `NodeId` | The id of the declared node the step ran for (see [below](#placing-a-step-on-the-trains-graph)); null when `NameWithheld` is set |
| `BranchPath` | The `Parallel` branch the step ran in, such as `Parallel#0/cocitation` (see [below](#steps-in-parallel-branches)); null outside any branch and when `NameWithheld` is set |
| `Attempt` | Which attempt of its manifest the run is, or null for a run with no manifest |

A step never carries a junction's input or output, the train's input or output, a failure's
message, or the state, instructions or criteria of a question. A failed junction is described by its
exception's type and its failure class only.

### Withholding an answer

Mark the enum or marker type a routing step asks about with
[`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) and its
answers are withheld from every junction event and every `trax.junction_run` row: the step records
that the question was asked and answered, with `AnswerWithheld` set, but not the option, score,
probability, confidence or the track taken on it.

```csharp
[TraxSensitive]
[Asks("Should this account be flagged for fraud review?")]
public sealed class FlagForFraud;
```

Sensitivity is decided by the type the question is about, with inheritance, so a type that
inherits the mark is withheld even when it was built at run time or lives in an assembly no scan
saw. The key is checked as well: a closed form of a marked generic type, a type nested in one, a
type that takes one as a type argument, and a key that merely shares a name with a marked type are
all withheld.

Withholding the answer withholds the path too. The steps a track runs would say which track it
took, so every step after a withheld route, whatever its kind, is published, handed to local
handlers and stored with its name as `(withheld)`, `NameWithheld` set and no `NodeId`, since a
node id names the track and the step. A later question or route
also has its `QuestionKey`, `Answer`, `Confidence` and `Decider` left out, with `AnswerWithheld`
set. `trax.decision` keeps the full answer either way, because a requeue replays it from there; the
journal's log writes it as withheld, and also withholds the question key, answer, track and decider
of every later decision, refusal and route of the run.

Some facts about a withheld track stay visible, by design, because they describe the run rather
than its data:

- how many steps ran after the route, their positions and kinds, and when each started and how long
  it took, which can differ from track to track
- a junction that failed there: its exception's type and failure class
- the run's own failure, whose junction `metadata.failure_junction` and the train's `Failed` event
  name as they always have (junction events themselves carry `FailureJunction` as null)
- a train started from a junction on the track, which is a run of its own, named, with steps of its
  own

A host for which these say too much should not turn junction events on for that train, and should
keep it off a broadcast feed.

### Steps on a track

Every step after a route carries `TrackPosition`, the route's position. Trax.Core reports where a
track starts but not where it rejoins the chain, so every step after a route counts as on its track.
A consumer that does not show a reader the answers should not show the names or question keys of
steps with a `TrackPosition` either, since they name the track: the SignalR sink and the GraphQL
broadcast view withhold them by default, as below. This errs toward hiding: a step that runs on
every track after the rejoin is hidden too.

### Placing a step on the train's graph

Each step carries `NodeId`, the id of the node `ChainGraph.From(train.DeclaredChain(), …)` draws for
the junction, question or route it ran for. An id names the step rather than its position: the
junction, decision or routing key, numbered within its chain or track, under the route and track it
sits on, as in `Switch<Source>#0/Papers/FetchPapers#0`. A junction's start and end carry the same
id, and a refused answer carries its question's. Read the train's graph once and lay a run's steps
over it by id. A step on a withheld track carries none.

### Steps in Parallel branches

The junctions, questions and routes of a `Parallel` step's branches run side by side, so their
steps interleave in the run's timeline. Each carries `BranchPath`, the branch it ran in, as Trax.Core
names it: the `Parallel` step's id and the branch's name, as in `Parallel#0/cocitation`, with the
path of an enclosing branch in front for a branch declared inside one. Draw a lane per branch by it.
Positions stay unique across the run.

A branch's tracks are its own. A route taken in one branch sets the `TrackPosition` of that branch's
later steps only; a sibling's steps keep the track they had at the fork, and after the join the run
is on the track it was on before the fork, because the branches' tracks end there.

A withheld answer withholds by the same lines. A withheld route in a branch withholds that branch's
later steps, and one taken before the fork withholds every branch. It does not withhold a sibling
running beside it, whose steps run alongside that track rather than on it and so give nothing of it
away. After the join everything is withheld, later branches included, because the steps after the
join follow every branch, the withheld one too. `BranchPath` is withheld with the name, since a
branch inside a track names the track.

## Where steps go

Each step goes to the host's `IJunctionEventHandler`s and, when [`UseBroadcaster`](/docs/sdk-reference/configuration/use-broadcaster)
has a transport, to the junction event handlers of other hosts. A junction event never reaches an
`ITrainEventHandler`, so a handler written for train events is never handed an event type it does
not know.

```csharp
public sealed class StepLogger(ILogger<StepLogger> logger) : IJunctionEventHandler
{
    public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var step = message.Junction!;
        logger.LogInformation("{Train} step {Position} {Name}: {State}",
            message.TrainName, step.Position, step.Name, step.State);
        return Task.CompletedTask;
    }
}

services.AddScoped<IJunctionEventHandler, StepLogger>();
```

On the host that runs the train, a handler is called on the run's path, from the run's scope, right
after the step is published, so the next junction waits for it: return quickly and queue anything
slow. On other hosts it is called by the receiver, from a fresh scope per message. Whatever a
handler throws is logged and never reaches the run or the other handlers.

Publishing never fails a run and never changes a junction's result. A failure to store, broadcast
or hand out a step is logged and swallowed. A custom `ITrainEventBroadcaster` is handed junction
events too, on the run's path, so it should queue rather than wait, and may route them apart from
train events.

## Stored steps

A background writer stores each step in `trax.junction_run`, one row per position, so the run never
waits on the database. It queues up to 4,096 steps and writes them in order and in batches through
a data context of its own. When the queue is full a step is dropped, counted and logged, rather than
holding up the run. Stopping the host drains what is queued.

Read a run's steps through `IDataContext.JunctionRuns` and `ForRun`, which orders them by position:

```csharp
var steps = await dataContext.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync(ct);
```

The rows can trail the live events by moments. A client following a running run subscribes first,
then reads the stored steps, and keeps for each position whichever is further along. A dropped step
is missing. A junction whose end was dropped stays `in_progress` after its run has ended, so read a
row's state together with its run's: an `in_progress` row of a run that finished is a step whose end
was not stored, not one still running.

**Retention.** A row is deleted with its run's metadata row, by the foreign key's cascade, so
[metadata cleanup](/docs/scheduler/admin-trains/metadata-cleanup), manifest pruning and any other
delete of metadata remove a run's steps with it.

The table ships in the core migration set: Postgres `055`, `057`, `060`, `067` and `068`, Sqlite
`020`, `022`, `025`, `030` and `031`.

### Attempt

A run of a manifest carries which attempt it is: 1 plus the manifest's failed runs since its last
completed or cancelled one, skipping dispatch attempts the scheduler requeued. It is read once,
when the run begins, from the manifest's 1000 most recent runs before this one (each one's state and
failure exception, newest first through the `(manifest_id, id)` index, Postgres `058`, Sqlite `023`),
so its cost does not grow with the manifest's history; a longer streak is reported as 1001. The run
waits on it for at most a second. A
run with no manifest carries none, and a read that fails or times out leaves it out and the run
alone.

## SignalR

The [SignalR sink](/docs/sdk-reference/configuration/use-signalr-hub#junction-events) sends
junction events to clients only when told to:

```csharp
effects.UseBroadcaster(b => b.UseSignalRHub(opts => opts.WithJunctionEvents()))
```

| Option | Sends |
|---|---|
| none (default) | No junction events |
| `WithJunctionEvents()` | Each step as a `TraxJunctionClientEvent`, through the `"JunctionEvent"` client method. A question's key and whether it was replayed, but not its answer or confidence. Every step on a track has its name `(withheld)` and its question key left out. |
| `WithJunctionAnswers()` | The same, with each question's answer and confidence and the names and keys of steps on a track. Answers to `[TraxSensitive]` questions, and every step after them, stay withheld. |
| `WithJunctionProjection<T>(...)` | Each step in a shape of your own, as `WithProjection` does for train events. Used whether `WithJunctionAnswers()` is called before or after it. |

Every client the hub admits receives every train's events, so the default payload leaves answers
out. A junction event passes the same `OnlyForTrains` filter as its train's events, and an
`OnlyForEvents` list must name the junction event types to let them through. When the sink's queue
is full it gives up junction events before train events.

## RabbitMQ

With [`UseRabbitMq`](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq), junction events
are published to a fanout exchange of their own, `<ExchangeName>.junctions` by default
(`trax.lifecycle.junctions`), set with `JunctionExchangeName`. It must differ from `ExchangeName`,
or `UseRabbitMq` throws.

The junction exchange is used only where steps are. A publisher declares it when it first has a
step to send, and a receiver binds it only on a host with an `IJunctionEventHandler`, each on a
channel of its own, so a junction exchange the broker refuses drops steps, logged, and never stops
train events. After a failure on the junction exchange, steps are dropped untried for a backoff
that starts at one second and doubles up to a minute, then the exchange is tried again; train
events are unaffected. A receiver takes train events only from the train exchange and steps only from the
junction exchange, and drops anything that arrives on the other. A receiver from before junction
events binds only the train exchange, so it never receives one.

No upgrade order is required. Upgrade the hosts that should show steps (they bind the junction
exchange once they have a junction event handler), then turn on `AddJunctionEvents` on the
workers; until a host binds it, the steps a worker publishes go nowhere. Rolling a worker back stops
its steps and nothing else. Because a run's steps and its train events travel through different exchanges, a
subscriber can see a run's `Completed` before its last step arrives; order by position and
timestamps rather than by arrival. The publisher's outgoing queue gives up junction events first
when it is full, so a busy run's steps never cost another run its outcome.

## GraphQL

Trax.Api forwards junction events to one subscription and reads the stored steps through one query:

| Field | Returns | Who may use it |
|---|---|---|
| [`onJunctionEvent(metadataId: Long!)`](/docs/sdk-reference/graphql-api/subscriptions#onjunctionevent) | The steps of one run, live | Exactly who would receive that run's train events: every train for the operations view, `[TraxBroadcast]` trains whose posture admits the caller for everyone else |
| [`operations.junctionRuns(metadataId, afterPosition, take)`](/docs/sdk-reference/graphql-api/queries#junctionruns) | The stored steps of one run, in order | The operations gate, as `operations.execution` |

The run is a required argument: there is no feed of every run's steps. Outside the operations view
a step loses host detail, as a train event does: the decider's type name, and a failure's exception
type unless it is a `TrainException`. It also carries no `answer` or `confidence`, and every step
after a route, of any kind, arrives with its name `(withheld)`, `nameWithheld` set and its
`questionKey` null, unless the host calls `AllowJunctionAnswersForBroadcastSubscribers()` on
`AddTraxGraphQL`: the same default the SignalR payload has. Its kind, position, state, timing and
failure class still arrive, so keep a train whose tracks must not be told apart off broadcast. The
operations view always sees answers. A host with an API-key or JWT scheme refuses a socket
that brings no credential in `connection_init`, even for a train marked `[TraxAllowAnonymous]`, so a
broadcast subscriber on such a host is an authenticated caller without the operations gate's role or
policy. A `[TraxSensitive]` answer, and every step the run withheld
after it, are withheld from every view. Both fields refuse a `metadataId` of 0 or less with
`TRAX_INVALID_ARGUMENT`.

Publishing a step to the subscription waits at most 250 ms on the run's path. A step that cannot be
published in time is dropped, and subscribers see it as a gap in `sequence`; a send still running
when the run is cancelled keeps its number. The feed is shared by every run on the host, so a gap
can come from another run's steps. The operations view recovers a gap by reading
`operations.junctionRuns` again; a broadcast subscriber cannot read it, and sees only the steps that
reach it.

## Dashboard

The dashboard's run page draws a **Junction Timeline** from the stored steps: one numbered row per
step, a junction on a track indented under its route and labelled "on track of step #N", with a bar placed against the run's start and coloured by state, a running step extending to now on
each refresh, a question's key, answer, confidence and whether it was replayed, a withheld answer
or step name shown as "withheld" (a question or route on a withheld track shows "withheld" for both
question and answer, with no confidence), and a failed step's failure class and exception type. The run's attempt is
shown when its rows carry one. It reads at most the first 500 steps and says so when the run
recorded more. While the run is running it re-reads only new and in-progress steps; once the run
has finished it reads every step twice more and stops, so a later change shows after a reload. A run with no stored steps shows a hint naming `AddJunctionEvents()`.
See [Dashboard: Metadata Detail Page](/docs/dashboard#metadata-detail-page).

## SDK Reference

> [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [IDataContext](/docs/sdk-reference/configuration/i-data-context)
