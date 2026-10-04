---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# Junction events are opt-in, carry no run data, and are kept as long as their run

A run's steps (each junction starting and ending, each question a routing step asks, each track it
takes) are published live and stored in `trax.junction_run` only on a host that calls
`AddJunctionEvents()`. They ride the existing broadcaster transport inside
`TrainLifecycleEventMessage`, under event types of their own and a `Junction` payload, and reach
only `IJunctionEventHandler`s. They carry names, positions, times, states, a failed junction's
failure class and exception type, and a question's answer summary, and never anything the run was
given or produced. Their rows go when the run's metadata row goes.

## Status

**Accepted.**

## Why this is written down

Because every one of these is the easy thing to loosen, and each loosening is a leak.

**Opt-in.** Every junction becomes up to two messages and two writes. A host that never shows a
timeline should not pay for one, and a consumer that subscribed to train events years ago should
not start receiving a new kind of event because a package was upgraded.

**A sibling event, not a new train event.** Before this, `TrainEventReceiverService` handed every
message to every `ITrainEventHandler`, and the shipped handlers treat an event type they do not know
differently: Trax.Api's GraphQL handler logs a warning per message, and the SignalR sink forwarded
anything that was not a data-change signal to every connected client. Adding `JunctionStarted` as
one more train event type would have reached both. So the receiver routes a junction event only to
`IJunctionEventHandler`s, the SignalR sink sends one only when configured with
`WithJunctionEvents()`, and nothing that handled train events before sees one now. They share the
broadcaster's queue, and the RabbitMQ queue drops them first when it is full, so a busy run's steps
never cost another run its outcome.

**A host that predates junction events never receives one.** Routing at the receiver only protects
receivers of this version. On RabbitMQ every receiver binds a fanout exchange, so junction events
are published to an exchange of their own, `<ExchangeName>.junctions` by default
(`JunctionExchangeName`), declared by the publisher and the receiver of this version, whose queue
binds both. A receiver from before binds only the train exchange, so a mixed fleet can be upgraded
in any order: an old receiver never sees a step, an old publisher never sends one, and a new
receiver gets steps from new publishers as soon as they start. The cost is that a step and its
run's own events no longer share one broker queue, so a subscriber can see a run's `Completed`
before its last step arrives; each carries its position and timestamps, and the API and dashboard
order by them.

The junction exchange is declared only where junction events are used: a publisher declares it when
it first has a step to send, on a channel of its own, and a receiver binds it only on a host with an
`IJunctionEventHandler`, also on a channel of its own. A junction exchange the broker refuses (one
declared elsewhere with another type, say) therefore drops steps, logged, and never closes the
channel train events use. A receiver takes a train event only from the train exchange and a step
only from the junction exchange, and drops anything that arrives from the other.

**Upgrading.** Nothing needs to happen in order. Upgrade the hubs that should show steps (they bind
the junction exchange once they have a junction event handler), then turn on `AddJunctionEvents` on
the workers; until a hub binds it, the steps a worker publishes go nowhere. Rolling back a worker
stops the steps and nothing else; a hub left on an older version never sees one.

**What a step carries.** A junction's input and output, the train's input and output, a failure's
message and the state a decider was shown can all hold user data, and a step goes to other
processes and, through the SignalR sink, to browsers. Train events already keep a failure reason
out of the default SignalR payload (`0016`) and present it by exception type in the API. A step
carries the exception's type and its failure class and nothing else about the failure. A question's
answer is new information that train events never carried, so `[TraxSensitive]` on the enum or
marker type a routing step asks about withholds it, and the track taken with it, from both the
event and the row (`0010` is extended to cover types for this purpose only). The mark is honoured
wherever the question's key is built from a marked type: a closed form of a marked generic type, a
type nested in one, a type that takes one as an argument, and a type that inherits the mark. When Trax.Core reports the type a question is about, that type decides,
so a type no scan saw (one built at run time, or in an assembly that does not reference Trax.Effect)
is still withheld when it inherits the mark; the key is checked as well. It fails closed: a key that
shares a name with a marked type is withheld too. The decision journal's log withholds the same
answers and tracks, and after such a track is taken, the key, answer, track and decider of every
later decision and route of the run, as the steps below do; `trax.decision` keeps them all, because
a requeue replays from there.

**What reaches a browser.** The SignalR sink sends every train's events to every connected client,
so its junction payload carries a question's key and whether it was replayed, but not its answer
or confidence. A host that wants answers in front of every client says so with
`WithJunctionAnswers()`, and a host that redacts its train events can shape junction events the
same way with `WithJunctionProjection()`. When the sink's queue is full it gives up junction events
before a train's own events, as the RabbitMQ queue does.

**Retention.** A step belongs to its run. The foreign key cascades, as `trax.decision`'s does, so
every existing delete of metadata, the scheduler's cleanup and manifest pruning included, removes
the steps without knowing the table exists.

## Considered options

**A new train event type with a nullable payload, delivered to every handler.** Rejected for the
reasons above: it changes what existing handlers receive.

**A separate message type and transport.** Rejected: a second RabbitMQ broadcaster and receiver
for the same payloads. A second exchange on the same broadcaster and receiver was taken instead.

**Writing a step on the run's path.** Rejected: a database that is slow or down would stall every
junction for its connection timeout. A background writer queues steps (4096), writes them in order
and in batches through a context of its own (never the run's tracked entities, so it never commits
a run's writes mid-run), and drops a step, counted and logged, rather than hold up a run.

## Consequences

**The stored timeline can trail the live one.** A late subscriber subscribes first, reads
`JunctionRuns.ForRun(id)`, and merges by position. A dropped step is missing, and a junction
whose end was dropped stays `in_progress` after its run has ended; the row's state is read together
with the run's. The writer reads the rows a batch's ends update in one query, so a burst costs two
round trips per batch rather than one per end, which keeps drops to a database that cannot keep up
at all. Reconciling a run's `in_progress` rows when it ends was not taken: the writer cannot tell
how a junction whose end it never saw ended, and a guessed state would be wrong where a stale one
is only out of date.

**Only `EffectJunction`s are steps.** A plain `Junction` in a service train runs no junction effects
and reports nothing. A junction skipped because an earlier one failed is not a step.

**Routing steps are told by their question.** Trax.Core reports a decision to the observer without
naming the step that asked, so a step is a `Choice` (Decide, Switch), `Score` (Scale) or `YesNo`
(Gate), and each track taken adds a `Route`.

**Withholding an answer withholds the path.** The steps a track runs would name the track, so every
step after a route whose answer is withheld, a junction, a question or another route, is published,
handed to local handlers and stored as `(withheld)`, with `NameWithheld` set, and a question's or
route's key, answer, confidence and decider left out with `AnswerWithheld` set. Every step after any
route carries `TrackPosition`, the route's position, so a consumer that does not show a subscriber
answers does not show it those names or keys either; the SignalR sink withholds them unless it sends
answers. A host's own `WithJunctionProjection` is used whether `WithJunctionAnswers` is called
before or after it. Trax.Core reports where a track starts but not where
it rejoins the chain, so a junction after the rejoin is counted as on the track too: this fails
closed and hides some names that could have been shown.

What stays visible on a withheld track is accepted rather than closed, and a host for which it is
too much does not turn junction events on: how many steps ran, their positions and kinds, and when
each started and how long it took, which can differ by track; the exception type and failure class
of a junction that failed there; the run's own failure, whose junction `metadata.failure_junction`
and the train's `Failed` event name as they always have (junction events themselves leave it out);
and a train started from a junction on the track, whose own run is named and has steps of its own.

**Decision observers compose.** Trax.Core finds one `IDecisionObserver` in the container. Trax now
registers a composite that tells every observer registered before `AddTrax` and every one Trax adds
(decision recording, junction events): required ones first, so a decision that could not be
recorded is not reported as made, then best-effort ones. An observer registered after `AddTrax` replaces the composite in the
container; while any of Trax's own observers is a part, the host refuses to start and every run
refuses too: decision recording so no decision is acted on unrecorded, and junction events because
withholding a track's steps depends on being told of every route. Each observer is built on its own.
A host observer known to be best effort without building it (an instance that is not required, or a
type that leaves `Required` to its default) that cannot be built is left out with a warning; any
other that cannot be built refuses the same way, with the reason. Decorating `IDecisionObserver` is
refused like any replacement.

**A manifest's run carries its attempt.** When the run begins, one query on a context of its own
counts the manifest's failed runs since its last completed or cancelled one, skipping the dispatch
attempts Trax.Scheduler requeued (`DispatchRequeued`), and the run's steps and rows carry 1 plus that.
A run with no manifest carries none rather than 1, because it is no attempt of anything, and a query
that fails leaves the attempt out and the run alone. It reads the state and failure exception of at
most the manifest's 1000 most recent runs below this one, newest first through
`ix_metadata_manifest_id_id`, so its cost does not grow with the manifest's history, and a longer
streak is reported as 1001. A run waits on it for at most a second before carrying on without an
attempt. Counting a streak in full was tried and dropped: its cost grows with the streak, row by
row from the table, until it outlasts that second.

**Publishing never fails a run.** A store, transport or handler failure is logged and swallowed.
Local handlers run on the run's path and must return quickly.

## Exemplars

- `JunctionEventsTests` pins the order, what a step carries and never carries, the withheld answer,
  that it is off by default, that a failing handler or transport does not change the run, and that
  decision observers compose.
- `JunctionEventRoutingTests` pins that a junction event reaches junction event handlers only.
- `RabbitMqJunctionExchangeTests` pins that a receiver bound only to the train exchange receives no
  junction event, while a current receiver receives both; that a refused junction exchange leaves
  train events flowing; that the junction exchange is declared only where steps are used; and that
  each exchange delivers only its own kind of event.
- `SignalRJunctionEventTests` pins that the SignalR sink sends steps only when asked, through the
  train filter it already applies, without answers unless asked for them, through a host's own
  projection when it has one, and that its full queue gives up steps first.
- `PostgresJunctionRunTests` and `SqliteJunctionRunTests` pin that the rows read back in order and
  go with their run, and on Postgres that a burst of several batches lands every end.
- `RabbitMqBroadcasterSenderTests` pins that a failing junction exchange is not tried again until
  its backoff passes.

Not covered: nothing stops a future field on `JunctionEventPayload` from carrying run data; the
tests check the fields that exist against known secrets, not the shape of every future field.

## Changelog

- **2026-10-03**: The journal's log withholds the later decisions and routes of a run on a withheld track.
- **2026-10-03**: The attempt reads at most 1000 runs again, and says it reads their state; counting a streak in full grew with the streak.
- **2026-10-02**: Questions and routes on a withheld track are withheld too; a replaced observer is refused while junction events are on; best-effort observers are built one by one; a host's junction projection wins in either order; the attempt is two indexed reads; the junction exchange backs off; what stays visible on a withheld track is listed.
- **2026-10-02**: The writer reads a batch's rows in one query; a junction whose end was dropped stays `in_progress`, said as such.
- **2026-10-02**: Junctions after a route carry its position, and after a withheld route their names are withheld.
- **2026-10-02**: The junction exchange is declared only where steps are used, on channels of its own, and each exchange carries only its own kind of event.
- **2026-10-02**: A run with no row has no steps; sensitivity is decided by the question's type; the journal's log withholds sensitive answers.
- **2026-10-02**: The attempt query is indexed, reads a bounded number of runs, and is timed out.
- **2026-10-02**: An observer registered after `AddTrax` refuses the host while decisions are recorded.
- **2026-10-02**: Junction events have a RabbitMQ exchange of their own.
- **2026-10-02**: Sensitive question types are matched in every form of their key; the SignalR payload leaves answers out unless asked, and its queue drops steps first.
- **2026-10-02**: A manifest's run carries its attempt.
- **2026-10-02**: Recorded.
