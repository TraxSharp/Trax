---
layout: default
title: Recovery
description: "The Recovery sample: a crashed run resumes without asking its model twice, a topic map runs signals in parallel, and state machines run trains."
parent: Samples & Deployment
nav_order: 3
---

# Recovery

A train asks a model which track to take, a later step crashes, and the retry takes the same tracks
without paying for the model again. The Recovery sample makes that visible, live against its own host. Its page has
two views. **Food order** places an order with a state machine whose `Ordered` state runs a train, from a laptop
panel, and follows it on a phone panel that listens on a socket of its own. **Workflows** runs a refund that asks a
model two questions, what went wrong with the order and whether to pay it, and crashes once. Each view shows the
real C# with the acting line lit and a timeline drawn as a fixed grid. On Workflows a model panel shows each question
as it was put to the model and the answer it gave. When the refund crashes while paying, attempt 2's answers are
marked **replayed** and take milliseconds instead of the model's second or so. The refund stores its outcome in a
checkpoint before it emails the customer, so when it crashes while emailing, attempt 2 starts from the checkpoint:
its row shows every step before it restored and runs only the email, and nothing is paid twice. Progress pills
follow the run through **Runs**, **Breaks** and **Recovers**. A second workflow, the topic map, runs three similarity
signals side by side in parallel branches. Two more state machines run trains as well: one system-owned instance per
partition of a scholarly index ingests it, and each user builds their own topic map through a wizard whose
`Building` state runs the map's train.

It proves six features working together, against Postgres, in one process:

| Feature | What the sample shows | Page |
|---|---|---|
| Train decisions | A `Switch` (what went wrong with the order) and a `Gate` (whether to pay it), answered by an `IDecider` | [Decisions](/docs/core/decisions) |
| Retries replay decisions | The manifest's automatic retry replays every recorded answer whose state hashes the same, asks afresh when the data changed, and asks afresh on purpose with `askAfresh` | [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) |
| Checkpoints and resume | The refund checkpoints its outcome before it emails the customer; the manifest's retry resumes after it, and an operator resumes the failed run at `NotifyCustomer` with `resumeExecution` or the dashboard's **Resume** | [Resuming from a checkpoint](#resuming-from-a-checkpoint) |
| Junction events | `onJunctionEvent` and `operations.junctionRuns` drive the page | [Junction Events](/docs/effect/junction-events) |
| Parallel branches | The topic map's three signals run side by side; a failed branch fails the run by name, and only the step after the join writes | [The topic map](#the-topic-map) |
| State machines that invoke trains | System-owned instances started from a train, an unsure output routed by a guarded `OnDone`, a failed ingest retried by entering its state again, a user's wizard whose result no client can write, and a draft rebuilt after its host is killed mid-run | [The state machines](#the-state-machines) |

The code is in `Trax.Samples/samples/Recovery`; its tests are in
`Trax.Samples/tests/Trax.Samples.Recovery.E2E`.

## Run it

Needs the .NET 10 SDK, Node.js 20 or later, and Docker. From the `Trax.Samples` folder:

```bash
docker compose up -d database
dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
```

In a second terminal:

```bash
cd samples/Recovery/Trax.Samples.Recovery.Client
npm ci
npm run dev
```

Open `http://localhost:5173`. The host listens on `http://localhost:5260`, with the dashboard at
`/trax` and the GraphQL IDE at `/trax/graphql`, both in Development only. When your Postgres is not
on 5432, start the host with
`ConnectionStrings__TraxDatabase="Host=localhost;Port=<port>;Database=trax_recovery;Username=trax;Password=trax123"`.

## What you'll see

The page opens on **Food order**. Pick a scenario and press **Place order**:

1. **Order a pizza.** The laptop starts an order (`start`, which mints the order's id on the server), adds a
   Margherita (`addItem`; the server prices it at $12.00), and presses Place order twice, 120 ms apart, under one
   request id, as a double click sends it. The server answers the second press from the first, so there is one
   order and one run. `Ordered` invokes `DeliverOrderTrain`: the code panel switches from `OrderMachine.cs` to
   `DeliverOrderTrain.cs` and lights each junction as it runs, the timeline fills the train's row
   (`SendToRestaurant`, `ChargeCard`, `Cook`, `PickUp`, `Deliver`), and the run's outcome moves the order to
   `Delivered`. The phone follows every change by `machineChanged` on a socket of its own. The file tabs flip
   between the two files by hand; a tab picked holds until the next order.
2. **The restaurant's tablet times out.** The tablet does not answer the first call, `SendToRestaurant` tries it
   again inside the same run, and the order is delivered. The phone says the restaurant has not answered yet, and
   the `SendToRestaurant` box is marked **attempt 2**.
3. **The restaurant is closed.** The run fails in `SendToRestaurant`, before `ChargeCard`, and its outcome moves
   the order to `Cancelled`; the phone says the card was not charged.
4. **Left in the cart.** The button reads **Leave it in the cart**: the order is started and filled, and never
   placed. The cart's timer clears it on the server, and both screens hear `Cleared` by push. The timeline names
   the wait from the `dueAt` the server answers with. The timer is 30 minutes (`OrderMachine.CartWait`); to watch it
   fire, start the host with `Recovery__CartWait=00:00:15` and `Recovery__MachineSweepInterval=00:00:01`. Without
   the second, the timer fires at the next sweep of the machines, up to a minute after it is due.

Then **Workflows**:

1. **Refund, A-1001, crash once while emailing, Run.** These are the defaults. Attempt 1 runs `LoadRefundCase`,
   asks `Problem` (what went wrong: `Damaged`, 91% sure, since the order arrived broken), runs `RequestPhotos` on
   that track, asks `ApproveRefund` (93% yes: the `Yes` track), pays in `IssuePayment`, stores the `RefundOutcome`
   in a checkpoint, and crashes in `NotifyCustomer`. The model panel shows each question's instructions and
   options, then the answer and how long it took; while a question is being asked, it shows the next one waiting
   for the model. The controls count down to the retry. A few seconds later the manifest's retry starts from the
   checkpoint: its row marks every box before `NotifyCustomer` restored, it runs only `NotifyCustomer`, neither
   question is asked again, and nothing is paid twice. Neither **Change the data during the backoff** nor **Ask
   afresh** is offered during this backoff: both questions come before the checkpoint.
2. **Crash once while paying, Run.** Attempt 1 crashes in `IssuePayment`, before the checkpoint. The retry runs the
   chain from the top and replays both answers: the model panel badges them **replayed** and the question boxes read
   `Damaged, replayed` and `Yes, replayed`. It takes the same tracks, pays and emails the customer.
3. **Change the data during the backoff.** Pressed while the retry counts down, it records an earlier refund on the
   order. The retry is still queued to replay attempt 1, but the earlier refunds are in the state of both questions,
   so neither hashes the same and both replays are refused. The model is asked both again: the problem is still
   `Damaged`, but approval drops to 58%, between the bars, and the refund takes the `Unsure` track to
   `QueueForReview` instead of being paid. The model panel badges both **asked afresh: state changed**.
4. **Ask afresh during the backoff.** It calls `triggerManifest(externalId, askAfresh: true)`, so the retry starts
   at once and asks both questions afresh: **asked afresh: on purpose**.
5. **Another track.** Orders A-1002 and A-1003 crash too. The model says A-1002 never arrived (`TraceParcel`) and is
   unsure about paying $420 (62% yes), and says the customer no longer wants A-1003 (`CheckReturnWindow`) and
   declines it after two earlier refunds (23% yes). Their runs take the review and decline tracks; each track's box
   is named by the junction the run took.
6. **Ask afresh after a run.** Once a run has completed, **Ask afresh** calls
   `requeueExecution(id, askAfresh: true)` on its last execution, a run of its own outside the manifest, shown as a
   `requeue` row. A requeue runs the chain from the top and asks both questions again.
7. **Topic map, crash once, Run.** Attempt 1 runs `LoadCorpus`, then three branches at once: `embedding`,
   `cocitation` and `authors`. The `cocitation` branch counts shared references, asks `SameTopic` (do papers that
   cite the same works share a topic in this slice?), and crashes in the step on the track the answer picks. The
   run fails with a `BranchesFailedException` naming `Parallel#0/cocitation`, and `CombineSignals`, the join, never
   runs, so nothing is written. The retry replays the answer and completes. The timeline lays the three branches'
   boxes side by side in the row. The three slices on the picker take the gate's three tracks with the demo model:
   2016 to 2025 trusts shared references (`Yes`, 0.87), 2021 to 2025 is unsure (0.52) and dampens them, 2022 to
   2025 ignores them (`No`, 0.28).

Case files and armed crashes live in memory. A run started before the host restarts has lost its
case file, so every retry that reads it again fails and the manifest dead-letters (a refund that
crashed while emailing resumes after its checkpoint and does not read it again). A requeue would fail the same way, so the page
offers no re-run once a run is dead. It offers **Resume from NotifyCustomer** then instead, and only then: when every
retry failed and the latest failed attempt's run graph says it can resume at `NotifyCustomer#0`, the button calls
`resumeExecution(id, from: "NotifyCustomer#0")`, the operation behind the dashboard's **Resume from here**, and the
`resume` row runs only `NotifyCustomer`, from the outcome the checkpoint stored. While the run is alive its own retry
already resumes from the checkpoint, and a run is resumed to completion once.

The same over GraphQL, with the header `X-Api-Key: recovery-operator-key-do-not-use-in-production`:

```graphql
mutation {
  dispatch {
    startRun(input: { scenario: REFUND, orderId: "A-1001", crashOnce: true, crashWhile: PAYING }) {
      output { runId manifestId manifestExternalId }
    }
  }
}

query {
  operations {
    executions(manifestId: 1, order: OLDEST) { items { id trainState failureJunction } }
  }
}

query {
  operations {
    junctionRuns(metadataId: 42) { position kind name state answer replayed trackPosition attempt nodeId }
  }
}
```

`crashWhile: EMAILING` crashes the refund in `NotifyCustomer` instead; left out, it crashes while
paying. The topic map starts the same way, with `scenario: TOPIC_MAP` and, optionally, `fields`, `fromYear`
and `toYear`. Its run graph, the train's declared chain with the run's steps laid on it:

```graphql
query {
  operations {
    runGraph(metadataId: 42) {
      nodes { id kind state tracks { name taken nodes { id kind state trackTaken } } }
    }
  }
}
```

## How it works

### The host

One ASP.NET process holds the GraphQL API, the scheduler, its local workers and, in Development,
the dashboard. The parts that make recovery work:

```csharp
using Trax.Core.Decisions;                          // IDecider
using Trax.Effect.Data.Extensions;                  // AddDecisionRecording, AddJunctionEvents
using Trax.Effect.Data.Postgres.Extensions;         // UsePostgres
using Trax.Effect.Decisions.SystemOne.Extensions;   // AddNimbleDecider (only with Recovery:Model = Nimble)
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;    // SaveTrainParameters
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

builder.Services.AddSingleton<IDecider, DemoDecider>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()      // requeueExecution reads a run's saved input
                .AddDecisionRecording()     // trax.decision, and replay on a retry or requeue
                .AddJunctionEvents()        // each junction, question and track, live and stored
        )
        .AddMediator(typeof(DemoDecider).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // Demo speed only: watch a retry within seconds.
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1))
                .DefaultRetryDelay(TimeSpan.FromSeconds(4))
                .RetryBackoffMultiplier(1.0)
                .MaxRetryDelay(TimeSpan.FromSeconds(10))
                .ConfigureLocalWorkers(w => w.PollingInterval = TimeSpan.FromMilliseconds(250))
                // The scheduler's own runs, two a second at this polling: sweep them.
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = builder.Configuration.GetValue(
                        "Recovery:SchedulerRunRetention", TimeSpan.FromMinutes(10));
                    cleanup.CleanupInterval = builder.Configuration.GetValue(
                        "Recovery:CleanupInterval", TimeSpan.FromMinutes(1));
                })
        )
);
```

Every train that runs a decision needs `AddDecisionRecording()` on the hosts that run it; a host
that runs a deciding train without it refuses to start (see
[A host that does not record](/docs/effect/decisions#a-host-that-does-not-record)). The scheduler
values above are for a demo; the defaults (a five-minute retry delay that doubles) are the right
ones for real work. Polling every second, the scheduler records about 170,000 runs of its own a day.
`AddMetadataCleanup` with no train added sweeps only the internal trains, after ten minutes here
(`Recovery:SchedulerRunRetention`, checked every `Recovery:CleanupInterval`), so the scenario runs,
their junction runs and their decisions stay for the page to read.

The refund's state carries a `[TraxSensitive]` member (the customer's email). Without a state hash
key, decision recording writes no hash for a state that can hold a sensitive member, and such an
answer is never replayed. So `appsettings.Development.json` holds a fixed demo key:

```json
{
  "Trax": {
    "Decisions": {
      "StateHashKey": "<base64 of at least 32 bytes>"
    }
  }
}
```

A real host keeps its key with its other secrets, and every process that may run a retry uses the
same one. See [Keying the state hash](/docs/effect/decisions#keying-the-state-hash).

### Who sees what

The page needs each question's answer and the names of the junctions on a track. Over
`onJunctionEvent`, only the operations view carries those; a subscriber outside it sees the run's
shape with answers and track steps withheld. The sample exposes the operations namespace behind a
role, and registers two demo keys in Development only:

```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add("recovery-operator-key-do-not-use-in-production", id: "operator", "Operator")
            .Add("recovery-viewer-key-do-not-use-in-production", id: "viewer", "Viewer"));

builder.Services.AddTraxGraphQL(graphql =>
    graphql.ExposeOperationQueries().ExposeOperationMutations().GateOperations(roles: "Operator"));
```

The refund train carries `[TraxBroadcast]` and `[TraxAuthorize(Roles = "Operator,Viewer")]`,
so the viewer key follows its steps through the broadcast view. The topic map's train carries
`[TraxAuthorize(Roles = "Operator")]` and no `[TraxBroadcast]`: the `topic-map` machine invokes it, and a
user's own machine may not invoke one that broadcasts every run to every subscriber. Each event that
enters the invoking state requires `Operator` too, which the host checks at startup, and the page
follows the run's steps through the operations view.

Once a token scheme is registered, a subscription socket without a credential is refused at
`connection_init`, so a "public" watcher still needs a key. The alternative to an operator key is
`AllowJunctionAnswersForBroadcastSubscribers()`, called inside `IsDevelopment()` only.

Outside Development no key exists, the operations namespace answers no one and the dashboard is
not mapped.

### The trains

Every step is an `EffectJunction`: a plain `Junction` emits no junction events. The refund asks the
model two questions, and stores its outcome before it emails the customer:

```csharp
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
public class ApproveRefundTrain : ServiceTrain<RefundInput, RefundResult>, IApproveRefundTrain
{
    protected override Task<Either<Exception, RefundResult>> Junctions() =>
        Chain<LoadRefundCase>()
            // Ask the model: what went wrong with this order?
            .Switch<RefundCase, Problem>(problem =>
                problem
                    .When(Problem.Damaged, then => then.Chain<RequestPhotos>())
                    .When(Problem.Lost, then => then.Chain<TraceParcel>())
                    .When(Problem.Unwanted, then => then.Chain<CheckReturnWindow>())
            )
            // Ask the model: should we pay it?
            .Gate<RefundEvidence, ApproveRefund>(answer =>
                answer
                    .Yes(then => then.Chain<IssuePayment>(), atLeast: 0.8)
                    .No(then => then.Chain<DeclineRefund>(), below: 0.3)
                    .Unsure(then => then.Chain<QueueForReview>())
            )
            // Save progress: a retry starts here, so nobody is paid twice.
            .Checkpoint<RefundOutcome>()
            .Chain<NotifyCustomer>()
            .Resolve();
}
```

Every `Switch` track produces `RefundEvidence` and every `Gate` track `RefundOutcome`, because after
a routing step the chain can rely only on what every track produces. `RefundEvidence` is what the
approval question is about: the amount, the earlier refunds, the problem and what checking it found.
`RefundOutcome` is all `NotifyCustomer` reads, which is why the checkpoint stores it; see
[Resuming from a checkpoint](#resuming-from-a-checkpoint).

A replay needs the state at each decision to hash exactly as it did the first time, so the states
hold only what the model reads and nothing that changes between attempts: no timestamp, no random
id, no cache. `LoadRefundCase` copies the order's amount, reason and earlier refunds into
`RefundCase`, and the `Switch` track copies the amount and earlier refunds on into `RefundEvidence`,
because the hash covers the state and nothing the decider might look up elsewhere. That is also why
a change to the earlier refunds refuses both replays.

### The model

`DemoDecider` implements `IDecider`. It waits 0.5 to 1.5 seconds, as a model would, and answers
from the state alone, so the same state always gets the same answer:

```csharp
public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
{
    await Task.Delay(Latency(), cancellationToken);
    var answers = new Dictionary<string, Answer>();
    foreach (var question in request.Questions)
        answers[question.Key] = (request.State, question) switch
        {
            (RefundCase refund, ChoiceQuestion) => NameProblem(refund),          // ChoiceAnswer
            (RefundEvidence evidence, YesNoQuestion) => ApproveRefund(evidence), // YesNoAnswer
            (CoCitationEvidence e, YesNoQuestion) => SameTopic(e),               // YesNoAnswer
            (MatchEvidence e, YesNoQuestion) => SameWork(e),                     // YesNoAnswer
            _ => throw new InvalidOperationException($"Cannot answer {question.Key}."),
        };
    return new DecisionResult(answers);
}
```

Set `Recovery:Model` to `Nimble` and `Recovery:Nimble:Endpoint` to a Nimble server you run, and the
host calls `AddNimbleDecider` instead. See [Nimble](/docs/effect/decisions#nimble).

### Nimble on a Mac

Nimble's own server, `nimble/serving/server.py`, runs SGLang and needs an NVIDIA GPU. On Apple
Silicon the sample's `nimble-mac/serve.py` serves the same model through Nimble's MLX scorer,
answering `POST /v1/systemone` on `http://127.0.0.1:8000` in the shape `AddNimbleDecider` reads, and
`nimble-mac/prepare.py` downloads the adapter and the Qwen3.5-9B base it pins and merges them once.
Both run from a checkout of `bespokelabsai/nimble`, with Python 3.12, about 40 GB of disk and room
for an 18 GB model; the README in `samples/Recovery` has the commands. Then start the host with:

```bash
Recovery__Model=Nimble Recovery__Nimble__Endpoint=http://127.0.0.1:8000/v1/systemone \
  dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
```

`AddNimbleDecider` accepts plain HTTP only to a loopback address, so this needs no certificate. The
server answers one request at a time, about a second a question on an M3 Pro, and nothing leaves the
machine.

A real model reads the case, so its answers can differ from the demo decider's. Nimble's latest release ships no fitted probability temperature and is
served at 1.0, and the gate's bars were chosen for the demo decider, so check them against your own
cases before trusting them.

### Starting a run as a one-off manifest

Decisions are replayed only by a manifest's automatic retry, a requeue of its dead letter, or
`requeueExecution`. A train queued through `queueTrain` or run through a mutation never replays, so
the page's **Run** has to create a manifest. Trax.Api has no GraphQL operation for a one-off
manifest, so the sample adds one, a `[TraxMutation]` train whose junction calls
`ITraxScheduler.ScheduleOnceAsync`:

```csharp
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxMutation(GraphQLOperation.Run, Description = "Starts a recovery demo run as a one-off manifest")]
public class StartRunTrain : ServiceTrain<StartRunInput, StartRunOutput>, IStartRunTrain { ... }

// in its junction
var manifest = await scheduler.ScheduleOnceAsync<IApproveRefundTrain, RefundInput, RefundResult>(
    $"recovery-{runId}",
    new RefundInput { RunId = runId, OrderId = orderId },
    TimeSpan.Zero,
    options => options.MaxRetries(2));
```

`MaxRetries(2)` allows three attempts. The mutation returns the manifest's id, which the page uses
to find each attempt with `operations.executions(manifestId:)`.

### Crashing once without changing the input

A retry replays only when the manifest's input is byte-identical between attempts, so a "crash
here" flag cannot live in the input. `FaultInjector` is a singleton keyed by the run id the input
already carries. `startRun` arms it, and the crashing junction fires it once. In `IssuePayment`:

```csharp
if (faults.TryFire(refund.RunId, CrashPoint.RefundTrack))
{
    await Task.Delay(pace.StepDelay);
    throw new TimeoutException(
        "The payment provider did not answer in time (timeout injected by the demo)."
    );
}
```

**Crash once while paying** arms `CrashPoint.RefundTrack`. Each step the approval can route to
(`IssuePayment`, `DeclineRefund`, `QueueForReview`) fires it first, before doing its work, so the
crash lands on whichever track the gate picks, after both questions and before the checkpoint, and a
retry never pays twice. **Crash once while emailing the customer** (`crashWhile: EMAILING`) arms
`CrashPoint.RefundEmail`, which `NotifyCustomer` fires the same way, after the checkpoint. `startRun`
arms either only after the manifest is scheduled.

Killing the worker process would not show a recovery: a killed run is failed by stuck-job recovery
much later, or at the next start, not resumed.

### Resuming from a checkpoint

Before the checkpoint, a refund has loaded its case, asked the model twice, checked the problem and
paid, declined or queued the refund. A crash in `NotifyCustomer` used to repeat all of it, payment
included, because a retry runs the chain from the top. `.Checkpoint<RefundOutcome>()` stores the
`RefundOutcome` in Memory when the run reaches it, with the track each routing step took, in
`trax.checkpoint`. Nothing else is stored. The state must come back from JSON as the same value, so
`RefundOutcome` is a sealed record of data; the host refuses to start otherwise and names the member.

**A manifest's retry now resumes by itself.** When the failed run has a checkpoint, the retry is
queued to resume after it: it runs `Junctions()` again, skips every step before the checkpoint in
place, puts the stored `RefundOutcome` back in Memory and runs only `NotifyCustomer`. Neither
question is asked and `IssuePayment` does not run: all of them come before the checkpoint. That is
also why the page offers no **Ask afresh** or **Change the data during the backoff** while a refund
that crashed while emailing waits for its retry. A crash while paying comes before the checkpoint,
so that failed run has none, and its retry runs from the top and replays both answers. A dead
letter's requeue resumes the same way; `requeueExecution` still reruns from the top.

**What the operator sees.** On the failed run, `operations.runGraph` sets `canResume`, marks the
checkpoint's node `checkpointed` and every node after it that a resume can start at `canResume`;
nothing before the checkpoint can. The dashboard's Metadata Detail page offers **Resume** beside
**Re-queue**, puts a **checkpoint** badge on the stored step of its run graph and a **Resume from
here** on each step the graph allows; both call `IOperationsService.ResumeExecutionAsync`, the call
behind `resumeExecution`. On the resumed run, every node before its resume point is `RESTORED`: it
was skipped, not run, and a step on a track the failed run did not take is `SKIPPED`. On a failed
refund, `NotifyCustomer#0` can be resumed at and `IssuePayment` cannot. Neither surface ever shows
what a checkpoint holds.

```graphql
query {
  operations {
    runGraph(metadataId: 42) { canResume nodes { id kind state canResume checkpointed } }
  }
}

mutation {
  operations {
    resumeExecution(id: 42, from: "NotifyCustomer#0") { success message id }
  }
}
```

`resumeExecution` resumes a failed or cancelled run, at the step `from` names or, without it, after
its latest checkpoint. It answers with the work queue entry's id, like a requeue, and the run is not
one of the manifest's executions. One resume of a run may be queued at a time: while the manifest's
retry is queued it already resumes the failed run, and a second resume is refused. The page offers
**Resume from NotifyCustomer** once the run is over for that reason. Once a resume of a run has
completed, its work is done and a further resume of it is refused; requeue it to run it again from
the top. See
[Checkpoint](/docs/sdk-reference/train-methods/checkpoint) for what may be stored and where a run
can resume.

### Changing the data during the backoff

`changeCaseData` edits the order the next attempt will read, not the manifest's input. The retry is
therefore still linked to the failed run (`replay_decisions_of`), and the replay itself refuses the
answer: its recorded `state_hash` no longer matches the state the question is asked about now. The
refusal is stored as `replay_refused` in the new row's answer. The run is not marked
`replay_abandoned`, which is kept for a replay that could not be honoured at all (the named run gone,
a host that does not record).

### The topic map

The topic map reads a corpus of 28 made-up papers in three fields, shaped like works from a scholarly
index (title, abstract, year, authors, the works each one cites, concepts). It makes no network
calls. The papers and the pairs the map writes live in a `topic_map` schema beside Trax's tables,
through a `DomainDataContext`; at startup the host creates the schema with
`EnsureSchemaCreatedAsync` and adds every paper not already there, so a restart adds nothing.
`EnsureSchemaCreatedAsync` is for demos and tests; a real application uses migrations.

```csharp
Chain<LoadCorpus>()
    .Parallel(signals =>
        signals
            .Branch("embedding", b => b.Chain<EmbeddingSimilarity>())
            .Branch("cocitation", b =>
                b.Chain<CountSharedReferences>()
                    .Gate<CoCitationEvidence, SameTopic>(gate =>
                        gate.Yes(t => t.Chain<TrustCoCitation>(), atLeast: 0.8)
                            .No(t => t.Chain<IgnoreCoCitation>(), below: 0.3)
                            .Unsure(t => t.Chain<DampenCoCitation>())))
            .Branch("authors", b => b.Chain<AuthorOverlap>()))
    .Chain<CombineSignals>()
    .Chain<FindHiddenTwins>()
    .Resolve();
```

`Parallel` is experimental: the project opts in with `<NoWarn>$(NoWarn);TRAXEXP001</NoWarn>`. Each
branch starts from a copy of Memory taken after `LoadCorpus`, runs in its own DI scope, and adds one
signal: `EmbeddingSignal` (a bag-of-words cosine over title and abstract, standing in for an
embedding model), `CoCitationSignal` (how many works two papers both cite, weighted by the track the
model chose) and `AuthorSignal`. `CombineSignals` reads all three as a tuple once every branch has
finished. The decision inside `cocitation` is the branch's own, recorded and replayed like any other.

Branches compute; the join commits. Because each branch has its own scope, and so its own
transaction, no branch writes. `CombineSignals` weighs the signals, keeps the pairs that score 0.3
or more and writes them in one transaction, replacing any pairs the same run wrote before.
`FindHiddenTwins` then lists the pairs that read alike (a similarity of 0.35 or more) but cite
nothing in common: the stormwater paper filed under hydrology and the green-roof paper filed under
urban ecology, and the two nitrate papers. A citation graph alone would never put them side by side.

The crash fires in the step on whichever track the gate picks, after the model has answered. The
branch fails, the default `CancelSiblings` policy stops any sibling still running, and the run
fails with one `BranchesFailedException`. Its `failureJunction` is
`Parallel#0/cocitation:<junction>`, so the execution names the branch. Every step a branch records
carries a node id under it, `Parallel#0/<branch>/...`, which is how `operations.runGraph` places it.

### The state machines

Two state machines run trains through the [`Invokes`](/docs/statemachine/invoking-trains): a
state names a train, entering the state queues one run in the transaction that moves the machine, and only
that entry of the state receives the run's outcome. The project opts in to the experimental `Parallel` with
`<NoWarn>$(NoWarn);TRAXEXP001</NoWarn>`, and the host adds `AddStateMachines(...)` before
`AddMediator(...)`, `AddJunctionProgress()` so a run can be cancelled from any host, and an
`ISnapshotPrincipal` that maps each demo key to a user.

#### One instance per index partition

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

```csharp
m.In(PartitionState.Ingesting)
    .Context<PartitionContext>()
    .Invokes<IIngestPartitionTrain, IngestPartitionInput, IngestPartitionResult>(ctx =>
        new IngestPartitionInput(ctx["source"]!.GetValue<string>(), ctx["month"]!.GetValue<string>()))
    .OnDone(PartitionState.NeedsReview,
        when: Input((IngestPartitionResult o) => o.Unsure).IsTrue(),
        reduce: KeepWhatWasWritten)
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

#### Build my topic map

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

```bash
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

The page drives its draft with `useServerMachine` from `@trax/state-machine/react`, over
`createMachineServer` from `@trax/state-machine/client`
([Driving an instance from React](/docs/sdk-reference/graphql-api/machines#driving-an-instance-from-react)). The
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

### The food order

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

### The page

The page is React 19, Vite, Apollo Client and `graphql-ws`. The host runs only persisted documents, so the page
sends every operation by an id and never its text: the topic map's by the ids the host generates for the machine
(`src/topicMap/topic-map.documents.json`), and every other one by the ids in `src/persisted-operations.json`, which
the host registers when it starts (`PageDocuments`). Over the socket the key travels in the `connection_init`
payload as `apiKey`, because a browser cannot set headers on a WebSocket upgrade. Both views keep their panels a fixed
size, so nothing moves as a run fills them.

The code panels import the sample's `.cs` files raw at build time. A panel shows the part that acts (a train's chain
from `Junctions()` to `Resolve()`, or a machine's states from its first `m.In(...)`), re-indents each chain so the
step names sit in one column, and fits its font to the longest line between 12 and 11 pixels; a line still too long
wraps under itself with a hanging indent, and the line numbers stay the file's.

**Food order.** Place order arms the scenario's trouble with `dispatch { orderTrouble }`, then sends the order
machine's own operations, `machines { order { start addItem placeOrder } }`, as `scripts/recordings/order.mjs`
does: `placeOrder` twice, 120 ms apart, under one request id. The laptop and the phone each subscribe to
`machineChanged` for the order on a socket of their own, and neither polls the order. The page finds the run
`Ordered` queued with `operations.executions(trainName:, startedAfter:)`, follows its steps with `onJunctionEvent`
and `operations.junctionRuns`, and reads `discover { orders { tabletCalls } }` while the run lasts, so a tablet call
that went unanswered shows as a failed try, a retry and the next try of `SendToRestaurant`. Everything on screen is
worked out from that list of timed events (`src/order/orderView.ts`): the phone's status and notifications, the
machine's line to light (the `.On(...)` of the event sent, the `m.In(...)` or outcome line of the state reached, the
`.Invokes(...)` line while the run is on its junctions, the `.After(...)` line when the timer clears the cart), the
train's junction to light, and the timeline's boxes. **Reset** discards an order still in its cart, since a user's
open carts count against their drafts.

**Workflows.** For each attempt the page:

1. polls `operations.executions(manifestId:)` until the attempt's execution exists;
2. subscribes to `onJunctionEvent(metadataId:)`;
3. then reads `operations.junctionRuns(metadataId:)` and merges both by `position`, keeping
   whichever copy of a step is further along, because the stored rows trail the stream and steps
   published before the subscription started reach it only through the table;
4. polls `operations.runGraph(metadataId:)` while the attempt runs, and reads it once more when it ends;
5. reads the decision journal as soon as a question's step arrives, and once more when the attempt ends.

Junction events say whether an answer was `replayed`, not why one was not, nor what was asked. The sample adds a
small `[TraxQuery]` train, `decisionJournal(metadataId)`, that reads `IDataContext.RecordedDecisions` and the run's
`ReplayDecisionsOf` and `ReplayAbandoned`. Each decision carries the question as Trax put it to the decider (its
instructions and each option's description), the answer the run acted on, the model and the decider, so the model
panel shows a card per question with its words and its answer: the choice and how sure (`Damaged, 91% sure`), or the
probability of yes and the track it took (`93% yes: the Yes track`). Its badge tells **replayed** from **asked
afresh: state changed** (a `replay_refused` reason) from **asked afresh: on purpose** (no replay link). While nothing
runs and the run graph's next node is a question the attempt has not answered, the panel shows that question waiting
for the model, with the words an earlier attempt was asked, and the code lights the line that asks it.

The timeline is a fixed grid with one row per attempt and one box per node of the train's declared chain, read from
the run graph (`src/workflowView.ts`): a junction, a question (with its answer), a routing step (named by the
junction on the track the run took), and the checkpoint. A `Parallel` step's branches are laid side by side in the
row. A box fills while its step runs. The checkpoint's box reads **saved** on the attempt that stored it and
**restored** on an attempt that started from it, whose earlier boxes are restored with it: a run that resumed records
only the steps after its resume point, and its graph marks the rest `RESTORED`. Each row ends in a status pill,
**Completed**, **Crashed** or **Recovered**. The page reads the graph as `allNodes`, one flat list at any depth, and
rebuilds the tree from each node's `parentId` and `track`; each read is numbered and an answer older than one already
drawn is dropped, so a slow answer never draws an older graph over the final one.

A `ROUTE` step carries `replayed: false` even when the decision it routes on was replayed; the timeline and the model
panel read the question's step.

**Resume from NotifyCustomer** sends `resumeExecution` with the node id `NotifyCustomer#0` for the latest failed
attempt, then reads the work queue entry it answers with until the entry names its run, and follows that run in a
`resume` row, as **Ask afresh** follows a requeue.

## The tests

`Trax.Samples.Recovery.E2E` boots the real host with `WebApplicationFactory` against the
`recovery_e2e_tests` database (port 5432, or `TRAX_TEST_PG_PORT`), with a counting decider in place
of the demo one, and asserts over GraphQL. `RestartTests` starts the host as two processes of its own, one after
the other, on the `recovery_restart_e2e_tests` database, so it can kill the first the way a crash does:

| Test | Proves |
|---|---|
| `RefundRun_PaymentTimedOutOnce_RetryTakesTheSameTrackWithoutAskingAgain` | The retry runs the whole chain and replays both answers, `Problem` and `ApproveRefund`; the state hash is keyed (`k1:`) |
| `RefundRun_CrashedWhileEmailing_RetryStartsFromTheCheckpoint` | Attempt 2 resumes after the checkpoint and completes; its junction events hold only `NotifyCustomer` and its journal no decision; the decider was asked each question once across both attempts |
| `A_crash_in_NotifyCustomer_resumes_from_RefundOutcome_without_paying_again` | The failed run's graph offers `NotifyCustomer#0` and not `IssuePayment`; `resumeExecution` and the dashboard's resume each run only `NotifyCustomer`, return the same result and draw the steps before as `RESTORED`; a completed resume is not resumed again; `TraxInvariants` finds nothing new |
| `RefundRun_OnOtherTracks_CrashesOnce_AndTheRetryReplaysTheDecisions` | Orders A-1002 (`TraceParcel`, review) and A-1003 (`CheckReturnWindow`, decline) crash once on their track, and the retry replays both answers |
| `RunWithNoCrashArmed_CompletesInOneAttempt` | One attempt, and the once manifest disables itself |
| `TriggerWithAskAfresh_DuringTheBackoff_RetryAsksTheModelAgain` | `triggerManifest(askAfresh: true)` makes a refund's retry ask both questions again |
| `RequeueExecution_ReplaysByDefault_AndAsksAgainWithAskAfresh` | `requeueExecution` replays; with `askAfresh: true` it asks again |
| `DataChangedDuringTheBackoff_RetryAsksAfresh_AndTakesTheTrackTheNewDataCallsFor` | The retry stays linked, refuses both answers for a changed state, asks afresh and takes `Unsure` to `QueueForReview` |
| `ViewerSubscriber_SeesTheShape_ButNotTheAnswersOrTheTrack` | The broadcast view gets no answers, and every step on a track is `(withheld)` |
| `TheSchedulersOwnRuns_AreSweptAfterTheirRetention` | The scheduler's own runs are deleted once past their retention |
| `Seeding_Twice_AddsNoWorks` | Seeding the corpus again adds nothing |
| `Run_OverTheWholeCorpus_MapsTopicsAndFindsTheHiddenTwins` | The map of the seeded corpus: 32 pairs written, the strongest pair, and exactly the two hidden twins |
| `Run_OverASlice_TakesTheCoCitationTrackTheModelChooses` | The three slices take the gate's `Yes`, `Unsure` and `No` tracks |
| `CrashedCoCitationBranch_FailsTheRunNamingIt_AndTheJoinWritesNothing` | The run throws a `BranchesFailedException` naming `Parallel#0/cocitation`, and no pair is written |
| `ScheduledRun_CrashedOnce_FailsNamingTheBranch_ThenEveryBranchRecordsItsSteps` | Attempt 1 fails naming the branch with nothing written; the retry replays the branch's answer, writes the pairs, records steps under all three branches, and `runGraph` draws them |
| `Discovery_StartsOneInstancePerPartition_AndARerunStartsNoneAndQueuesNothing` | Six partitions, six instances, six ingest runs; a second discovery starts none and queues nothing |
| `EachPartition_IsIngested_AndTheUnsureOne_GoesToReview` | Five instances reach `Ingested` and Crossref/2025-02 reaches `NeedsReview`, each holding counts and a fingerprint; `Approve` moves it on, and `Retry` from `Ingested` is a `no-transition` |
| `ACrashedIngest_Fails_AndARetry_RunsItAgainUnderANewToken` | A crashed ingest reaches `Failed` and is not retried by the scheduler; `Retry` queues a new run under a new token and the instance reaches `Ingested`, and a late delivery of either run's outcome moves nothing |
| `OnlyAnOperator_MayActOnAPartition` | The viewer key's `partitionAction` is refused with `TRAX_AUTHORIZATION`, and the instance neither moves nor queues a run |
| `AnOperator_CannotCancelTheBuild_ThroughTheRunOrItsEntry_OneByOneOrInBulk` | `cancelWorkQueueEntry`, `cancelExecution` and `cancelExecutions` refuse or skip the run a user's draft started, with one message, and the build still reaches `Built` |
| `Operators_SeeThePartitions_ReadOnly_AndNoUserReachesThem` | `operations.machineInstances` lists the system instances and has no context field; the system-owned machine has no field under `machines`, and no generic read is served |
| `TheWizard_BuildsTheMap_AndTheDraftKeepsAPointerToIt` | Fields, years, build: the run's outcome moves the draft to `Built` with the map's id and summary, the pairs are under that id, and a reload returns the same snapshot |
| `TheServerMintsTheDraftId_AndADiscardedDraftIsGone` | `start` mints the id and builds the first step; a discarded draft reads as no draft |
| `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNothingThatCountsTowardTheCap` | From `Built`, `StartOver` resets the draft to the first step, clears its due time, and leaves a draft that discards |
| `AnAutosaveIntoBuilt_IsRefused`, `WhileBuilding_AHandFiredOutcome_AndAnAutosave_AreRefused` | neither a save nor the outcome is served to a client; the real outcome still lands |
| `CancellingTheBuild_GoesBackToTheRange_AndCancelsItsRun`, `AFailedBuild_IsRebuiltByEnteringBuildingAgain_WithANewRun` | Leaving `Building` cancels its run; a rebuild is a new run |
| `ARebuiltMap_LeavesTheOldMapToTheSweep_AndKeepsTheOneTheDraftPointsAt`, `TheSweep_NeverDeletesAMapAStoredDraftPointsAt_NorOneItSawOnlyOnce` | After a rebuild, two sweeps delete the replaced map's pairs and keep the new one's; a map is deleted only on the second sweep that finds it unreferenced |
| `AnOrder_IsDelivered_AndTheServerPricesTheCart`, `PlaceOrderPressedTwice_MakesOneOrderAndOneRun` | The order runs all five steps and reaches `Delivered`; one request sent twice is answered once, a second request is `no-transition`, and the order has one run |
| `ATabletThatTimesOut_IsTriedAgain_AndTheOrderIsDelivered`, `AClosedRestaurant_CancelsTheOrder_BeforeTheCardIsCharged`, `ACartNobodyOrders_IsClearedByTheServer` | The tablet's second attempt is answered inside the one run; a closed restaurant fails the run before `ChargeCard` and the order is `Cancelled`; the cart's timer moves it to `Cleared` |

| `AHostKilledMidBuild_FailsTheRunOnTheNextStart_AndTheDraftIsRebuiltFromBuildFailed` | A host process killed mid-build leaves the run `InProgress`; the next host fails it on startup, the draft reaches `BuildFailed`, the seeded data is there, and a rebuild reaches `Built` |

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.E2E
```

### The browser tests

`Trax.Samples.Recovery.Browser` drives the built page in chromium with Playwright for .NET
([ADR 0049](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md)),
against the host started as a process on the `recovery_browser_tests` database. It builds the page with vite, serves
it at the origin the host's CORS policy admits, and forwards the page's requests and its socket to the host, so the
page's code runs unchanged and every request it makes is seen. Each test waits on a locator, a request or the
database, never a delay, and `TraxInvariants` is checked after every one:

| Test | Proves |
|---|---|
| `TheWizard_RunsEndToEnd_WithAdHocDocumentsRefusedAndGenericMutationsOff` | Fields, years, build to `Built` through the typed operations only; an ad-hoc document is refused, and no `stateMachine` field exists |
| `TheBuildOutcome_ArrivesByPush_WithNoPollingRequest` | The page makes no request between the `build` answer and rendering `Built` |
| `TheReviewTimer_ExpiresByPush` | A due review timer moves the draft to `Expired` on the server, and the page shows it with no request of its own |
| `AViewerRolesOperatorOnlyEvent_IsRefusedOnTheServer` | The viewer key's `build` is `forbidden`, and the draft's revision does not move |
| `TheDashboardAndGraphQL_ShowTheSameStateRevisionAndRuns` | `get`, the stored row and the dashboard's instance page agree on the state and revision, and on no live run |
| `ReloadingTheTab_ResumesTheServerMintedInstance` | A reload opens the kept draft at its step with one `start` in all and no save |
| `TwoTabs_SeeEachOthersTransitionsInRevisionOrder` | A step in one tab reaches the other by push, in order |
| `ADraftTheServerNoLongerHolds_IsForgotten_AndANewOneStarted` | A draft discarded elsewhere is pushed as `gone`; the page starts a new one, and a reload keeps that one |
| `StartingANewMap_DiscardsTheOldDraft` | **Start a new map** before a build discards the old draft before starting the next |
| `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNoneBehind` | **Start a new map** from `Built` resets the same draft with `StartOver`, with one start request in all and the review timer gone |
| `ADeployWithANewIR_ShowsClientOutdated` | A page built for another IR shows `client-outdated`, hides the wizard and makes no further request |
| `KillingTheHostMidBuild_RecoversThroughTheStuckRunPath_AndThePageFollows` | The next host fails the dead build and the page follows to `BuildFailed` by push, with no request of its own |

They need the page's npm packages (`npm ci` in `Trax.Samples.Recovery.Client` and in `Trax.Api.StateMachine`) and
chromium (`pwsh tests/Trax.Samples.Recovery.Browser/bin/Debug/net10.0/playwright.ps1 install chromium` after a
build):

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.Browser
```

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [Switch](/docs/sdk-reference/train-methods/switch) | [Gate](/docs/sdk-reference/train-methods/gate) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [Mutations](/docs/sdk-reference/graphql-api/mutations) | [Queries](/docs/sdk-reference/graphql-api/queries) | [Invoking a train](/docs/statemachine/invoking-trains) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances)
