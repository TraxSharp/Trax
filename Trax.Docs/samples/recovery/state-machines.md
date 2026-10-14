---
title: Recovery state machines
description: "The three state machines in the Recovery sample that run trains: index partitions, the topic map wizard and the food order."
type: explanation
area: samples
parent: Recovery
nav_order: 1
---

# Recovery state machines

Part of the [Recovery](/docs/samples/recovery) sample tour. Three state machines run trains through
[`Invokes`](/docs/statemachine/invoking-trains): a
state names a train, entering the state queues one run in the transaction that moves the machine, and only
that entry of the state receives the run's outcome. The project opts in to the experimental `Parallel` with
`<NoWarn>$(NoWarn);TRAXEXP001</NoWarn>`, and the host adds `AddStateMachines(...)` before
`AddMediator(...)`, `AddJunctionProgress()` so a run can be cancelled from any host, and an
`ISnapshotPrincipal` that maps each demo key to a user.

## One instance per index partition

The host also seeds 18 made-up records from two scholarly indexes, OpenAlex and Crossref, three months
each: six partitions. `DiscoverPartitionsTrain` (the operator mutation `discoverPartitions`) lists them and
starts one `source-partition` instance per partition with `IMachineInstances.Start`, keyed by
`(source, month)`. The machine is `SystemOwned()`: no user owns an instance, and no `stateMachine`
mutation reaches one.

```
Discovered --Ingest--> Ingesting --done, unsure--> NeedsReview --Approve--> Approved
                         │  ▲      --done-------> Ingested
                         │  └─Retry── Failed      (the run failed, or was reaped)
                         │  └─Retry── Cancelled   (an operator cancelled the run)
```

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Machines/SourcePartitionMachine.cs
        m.In(PartitionState.Ingesting)
            .Context<PartitionContext>()
            .Invokes<IIngestPartitionTrain, IngestPartitionInput, IngestPartitionResult>(
                ctx => new IngestPartitionInput(
                    ctx["source"]!.GetValue<string>(),
                    ctx["month"]!.GetValue<string>()
                )
            )
            // Tried in order: an unsure ingest goes to review, any other to Ingested.
            .OnDone(
                PartitionState.NeedsReview,
                when: Input((IngestPartitionResult o) => o.Unsure).IsTrue(),
                reduce: KeepWhatWasWritten
            )
            .OnDone(PartitionState.Ingested, reduce: KeepWhatWasWritten)
            .OnFailed(PartitionState.Failed)
            .OnCancelled(PartitionState.Cancelled);
```

**An unsure ingest goes to review through a guarded `OnDone`.** A run ends in one result, so "unsure" is
not an outcome of its own: the ingest says so in its output, and the `OnDone` edges are tried in order.
Crossref's February partition holds a paper whose title is close to one in the corpus, the model is unsure
whether they are the same work, and the instance lands in `NeedsReview`; the other five land in
`Ingested`. Either way the reduction keeps pointers, not rows: a fingerprint of what was written and the
counts of works created, merged and held for review. The works themselves are in
`topic_map.ingested_works`.

**Discovery is idempotent.** An instance's id is derived from its key, so running discovery again finds the
six instances it started, creates none and queues nothing. Starting an instance and sending it into
`Ingesting` are two writes; an instance a crash left in `Discovered` between them is sent on by the next
discovery, and one already past `Discovered` is left alone.

**A failed ingest is retried by entering `Ingesting` again, not by the scheduler.** An invoked run has no
manifest, so nothing retries it. Its failure moves the instance to `Failed`, and `Retry` moves it back into
`Ingesting`, which queues a new run under a new token. The old run's token is gone, so a late delivery of
its outcome lands nowhere. Because the machine is system-owned, the sample fires `Retry` (and `Approve`,
out of `NeedsReview`) through its own operator-only mutation, `partitionAction`, which calls
`IMachineInstances.Advance` as the system. That call makes the same checks a user's advance makes: `Retry`
only from `Failed` or `Cancelled`, and never an outcome trigger.

```graphql
mutation { dispatch { discoverPartitions(input: {}) {
  output { instancesStarted instancesSentToIngest partitions { source month records } } } } }

mutation { dispatch { partitionAction(input: { source: "OpenAlex", month: "2025-03", action: RETRY }) {
  output { state problem } } } }

query { operations { machineInstances(machine: "source-partition", ownerKind: SYSTEM) {
  totalCount items { id state hasLiveInvokedRun } } } }
```

Operators see the instances on the dashboard's State machines page and through
`operations.machineInstances`: state, timestamps and whether a run is live, never the context.

## Build my topic map

The `topic-map` machine is a wizard each user drives from the page: choose the fields, choose the years,
build. It is user-owned, so each demo key has drafts of its own, stored in `trax.snapshot_draft`. A draft is
created by the machine's declared start, `Start<TopicMapStart>(s => s.Reduce(Keep()).AnyAuthenticated())`: the
start input is its own record, never the context type, the server mints the draft's id, and the declared
reduction builds the first step from the initial context. No client writes a snapshot.

```
ChoosingFields --ChooseFields--> ChoosingRange --Build--> Building --done-------> Built
      ▲            ◀──Back──          ▲  ◀──CancelBuild──    │     --failed-----> BuildFailed
      │                               └───────Edit─────────── Built, BuildFailed, BuildCancelled
      │                                                      └──── --cancelled--> BuildCancelled
                          Building ◀──Rebuild── Built, BuildFailed, BuildCancelled, Expired
              Built --after 7 days--> Expired --Edit--> ChoosingRange
ChoosingFields ◀──StartOver── Building, Built, BuildFailed, BuildCancelled, Expired
```

**A built map waits seven days for review.** `Built` declares `.After(ReviewWindow).GoTo(Expired)`, a timer the
server runs: entering `Built` sets the draft's due time from the database clock, and the host's reconciler moves a
draft whose time has come to `Expired`, whether or not anyone has the page open. No client can send the timer's
trigger, and the page shows the move when the server pushes it. Until then it shows the review deadline, the
`dueAt` the server sends beside the `Built` snapshot. The reconciler wakes when the next due timer
falls due, so a timer fires on time. Beyond that it sweeps once a minute while it hears Postgres' notifications, which
matters only for a due time rewritten behind its back; `Recovery:MachineSweepInterval` sets a shorter sweep (the
browser tests use one second, because they make a timer due by rewriting the row, which the reconciler is not told
of).

`Building` invokes `IBuildTopicMapTrain`. The run's input is built on the server from the draft's choices,
with a run id of its own, which the pairs the join writes are keyed by; `OnDone` keeps that id as `mapId`,
with the number of papers and pairs and the co-citation track.

**Every event declares who may send it.** `Build` and `Rebuild` enter `Building`, so both declare
`.Requires(RecoveryRoles.Operator)`, the role `BuildTopicMapTrain` asks for; the other events declare
`.AnyAuthenticated()`, and only a draft's owner reads it. At startup the host compares every event that
enters an invoking state with the train's `[TraxAuthorize]`, and refuses to start when one would let a
caller run a train they could not run directly. The rule is checked on the server for every event, so a
viewer's `Build` is refused as `forbidden` before anything is written.

**Old maps are swept.** A rebuild writes a new map and moves `mapId` to it, which leaves the old one
behind, and a demo run's map is pointed at by nothing once its run ends. `TopicMapSweeper` deletes the
pairs of every map no stored `topic-map` draft points at, every five minutes
(`Recovery:TopicMapSweepInterval`). It decides from the drafts the server holds and never from what a
client sent. The build does not delete the map it replaces: another draft of the same user may still point at
it. A build writes its pairs a moment before its outcome points the draft at them, so a map is deleted only
once two sweeps an interval apart both found it unreferenced. **Start a new map** leaves no draft behind. A draft
in `Building`, or one a build's outcome put in `Built`, `BuildFailed` or `BuildCancelled`, is the record of that
build, so the server refuses to discard it (`discard-not-allowed`). From those states, and from `Expired`, the page
sends `StartOver` (any signed-in user), which resets the same draft to the first step with nothing chosen: from
`Building` it cancels the run, and leaving `Built` cancels the review timer. Before a build, the page discards the
draft and starts a new one. If the server refuses either, the page shows why and stays on the draft rather than
starting another. A map no draft points at any more goes with the next two sweeps.

**The demo stays under the draft cap.** Every visitor signs in with the same operator key, so every draft
the page starts belongs to one user, and the server caps the drafts one user holds (50 by default). Besides
leaving no draft behind when it starts a new map, the host sets `DraftTtl` to one day, so a draft a closed browser
abandons stops counting toward the cap after a day rather than the default 30, and a demo database that lives for
weeks does not fill it. One abandoned before a build is deleted then; one a build moved is kept as the record of
that build (one in `Built` until its review timer moves it to `Expired`). The cap itself stays on.

The page drives the wizard with a TypeScript twin generated from the machine by the `trax machine` CLI:

```bash from=Trax.Samples/samples/Recovery/README.md
dotnet run --project Trax.Cli/src/Trax.Cli -- machine generate \
  --assembly Trax.Samples/samples/Recovery/Trax.Samples.Recovery/bin/Debug/net10.0/Trax.Samples.Recovery.dll \
  --machine Trax.Samples.Recovery.Machines.TopicMapMachine \
  --ir-out Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Client/src/topicMap \
  --twin-out Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Client/src/topicMap \
  --corpus-out Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Client/src/topicMap \
  --engine-src Trax.Api.StateMachine/src --import-style specifier
```

The topic map is marked `[TraxMachine]`, so it is served as its own typed operations
under the [machines root](/docs/sdk-reference/graphql-api/machines),
`mutation { machines { topicMap { start chooseFields back build cancelBuild rebuild edit startOver discard } } }` and
`query { machines { topicMap { get } } }`, and the generic `stateMachine` mutations are not served at all. The host also runs only persisted documents: it registers the topic map's own when it starts,
and the page sends each by the id `topic-map.documents.json` lists, exported from the host's manifest for the IR the
twin was generated from (`TRAX_UPDATE_MACHINE_DOCUMENTS=1 dotnet test --filter TopicMapDocumentsTests` in
`Trax.Samples.Recovery.E2E` writes it again).

The page drives its draft with `useServerMachine` from `@traxsharp/state-machine/react`, over
`createMachineServer` from `@traxsharp/state-machine/client`
([Driving an instance from React](/docs/statemachine/drive-a-machine-instance-from-react)). The
server owns the draft; the twin only predicts:

- It starts the draft with `machines { topicMap { start } }`, which answers with the id the server minted, and moves
  it only with the machine's declared events, each its own mutation (`chooseFields(id, input: { fields },
  requestId)`, `build(id, input: { fromYear toYear }, requestId)`, ...), sent one at a time, each under its own
  request id.
- The twin tells the page which step it may take and what the step will show. The page shows that at once and
  sends it as `clientResult`; the server applies the event itself, refuses a result that differs from its own, and
  answers what it stored, which replaces the prediction. A refused step is rolled back and its code shown.
- Whatever else moves the draft (the build's outcome, the review timer, a recovery after a crash, a step from another
  tab) reaches the page on `machineChanged`, subscribed by its persisted id over the socket. The page never polls:
  while the draft is `Building` it sends nothing until the outcome is pushed. Every answer and push carries the
  draft's revision, and anything at or below the revision shown is dropped, so a slow answer never puts back a draft
  a push has moved on.
- The draft id is kept in the browser, so closing the tab and coming back opens the same draft at the same step. The
  start's request id is kept too until the start answers, so a reload in the middle retries it and gets the same
  draft rather than a second one. A draft the server no longer holds (discarded elsewhere, or expired) ends the
  stream with `gone`, and the page forgets it and starts a new one.
- After a deploy that changes the machine, the host answers the page's documents `client-outdated`: the page hides
  the wizard, shows the code and sends nothing more until it is reloaded.

**A client cannot write a build's result.** The host serves no save, so no client writes a topic map whatever state it
names, and the run's outcome (`Building.done`) has no client field: only the run's outcome moves a draft out of
`Building`. `CancelBuild` leaves `Building`, which cancels
the run: a run still queued is marked cancelled, and one already running stops at its next junction.

**What "recovers" means after a restart.** A run a machine invoked has no manifest, so the scheduler does
not retry it. When the host running a build dies, the run stays `InProgress` until a host starts and fails
it on startup recovery (or the stale-run reaper fails it, after `StaleInProgressTimeout`). The outcome
reconciler, which every host that registers machines runs, then moves the draft from `Building` to
`BuildFailed`. The draft and the seeded data are in Postgres, so both are still there, and `Rebuild` from
`BuildFailed` queues a new run on the host that is up.

## The food order

`OrderMachine` (`order`) is the smallest of the three, written to be read line by line beside the page's **Food
order** view, which drives it live, and beside its recording on traxsharp.net (`scripts/recordings/order.mjs`). A user fills a cart (`addItem`, priced by the server through a custom
reduction) and sends `placeOrder`, which enters `Ordered`; `Ordered` invokes `DeliverOrderTrain`, whose outcome ends
the order in `Delivered` or `Cancelled`. A cart nobody orders is cleared by the server's timer after 30 minutes
(`Recovery:CartWait` shortens it, for the recording and to watch **Left in the cart** on the page). The train's only rule is a signed-in caller (a bare
`[TraxAuthorize]`), matching `placeOrder`'s `AnyAuthenticated()`.

```
Cart --PlaceOrder--> Ordered --done-----------------> Delivered
  │                          --failed or cancelled--> Cancelled
  └──after 30 minutes──> Cleared
```

Two things the recording shows are worth knowing. Place order pressed twice under one request id is answered once:
the second answer replays the first, and a press under a new request id is `no-transition` from `Ordered`, so one
order makes one run and one charge. And the scheduler never retries a run a machine invoked, so a step that fails
fails the run, which is the order's `Cancelled`. The restaurant's tablet that does not answer
(`dispatch { orderTrouble(input: { trouble: TABLET_TIMES_OUT }) }`, armed for the next order) is therefore tried
again inside `SendToRestaurant`, and the machine sees one run that recovered; `discover { orders { tabletCalls } }`
lists both attempts. A closed restaurant (`RESTAURANT_CLOSED`) fails the run at `SendToRestaurant`, before
`ChargeCard`, so the order is `Cancelled` with nothing charged.

## SDK Reference

> [Invoking a train](/docs/statemachine/invoking-trains) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines](/docs/sdk-reference/graphql-api/machines)
