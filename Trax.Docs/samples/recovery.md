---
title: Recovery
description: "The Recovery sample: a crashed run resumes without asking its model twice, a topic map runs signals in parallel, and state machines run trains."
type: explanation
area: samples
nav_order: 4
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

## What it proves

Six features working together, against Postgres, in one process:

| Feature | What the sample shows | Page |
|---|---|---|
| Train decisions | A `Switch` (what went wrong with the order) and a `Gate` (whether to pay it), answered by an `IDecider` | [Decisions](/docs/core/decisions) |
| Retries replay decisions | The manifest's automatic retry replays every recorded answer whose state hashes the same, asks afresh when the data changed, and asks afresh on purpose with `askAfresh` | [Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions) |
| Checkpoints and resume | The refund checkpoints its outcome before it emails the customer; the manifest's retry resumes after it, and an operator resumes the failed run at `NotifyCustomer` with `resumeExecution` or the dashboard's **Resume** | [Resuming from a checkpoint](#resuming-from-a-checkpoint) |
| Junction events | `onJunctionEvent` and `operations.junctionRuns` drive the page | [Junction Events](/docs/effect/junction-events) |
| Parallel branches | The topic map's three signals run side by side; a failed branch fails the run by name, and only the step after the join writes | [The topic map](#the-topic-map) |
| State machines that invoke trains | System-owned instances started from a train, an unsure output routed by a guarded `OnDone`, a failed ingest retried by entering its state again, a user's wizard whose result no client can write, and a draft rebuilt after its host is killed mid-run | [Recovery state machines](/docs/samples/recovery/state-machines) |

The code is in `Trax.Samples/samples/Recovery`; its tests are in
`Trax.Samples/tests/Trax.Samples.Recovery.E2E`.

## Run

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

## Try it

The page opens on **Food order**. Pick a scenario and press **Place order**:

1. **Order a pizza.** The laptop starts an order (`start`, which mints the order's id on the server), adds a
   Margherita (`addItem`; the server prices it at $12.00), and presses Place order twice, 120 ms apart, under one
   request id, as a double click sends it. The server answers the second press from the first, so there is one
   order and one run. `Ordered` invokes `DeliverOrderTrain`: the code panel switches from `OrderMachine.cs` to
   `DeliverOrderTrain.cs` and lights each junction as it runs, the timeline fills the train's row
   (`SendToRestaurant`, `ChargeCard`, `Cook`, `PickUp`, `Deliver`), and the run's outcome moves the order to
   `Delivered`. The phone follows every change by `orderChanged` on a socket of its own. The file tabs flip
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
the dashboard. The parts that make recovery work (the demo decider is registered unless `Recovery:Model` is
`Nimble`, and `AddNimbleDecider` joins the effects when it is):

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Api/Program.cs
var model = builder.Configuration["Recovery:Model"] ?? "Demo";
var useNimble = model.Equals("Nimble", StringComparison.OrdinalIgnoreCase);
if (!useNimble)
    builder.Services.AddSingleton<IDecider, DemoDecider>();

// ── Trax ────────────────────────────────────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        {
            var configured = effects
                .UsePostgres(connectionString)
                .AddJson()
                // requeueExecution reads a run's saved input.
                .SaveTrainParameters()
                // Writes each decision to trax.decision and replays it on a retry or a requeue.
                // The refund's state holds a [TraxSensitive] member, so states are hashed under a
                // key: Trax:Decisions:StateHashKey (appsettings.Development.json holds a demo one).
                .AddDecisionRecording()
                // Publishes each junction, question and track live, and stores it in
                // trax.junction_run. Only EffectJunctions are steps.
                .AddJunctionEvents()
                // Records the running junction and reads a run's cancel flag between junctions, so
                // a machine leaving a state stops the run it invoked on whichever host runs it.
                .AddJunctionProgress();
// ...
        .AddMediator(typeof(DemoDecider).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // Demo speed only. The defaults (5 s and 2 s polling, a 5 minute retry delay that
                // doubles) suit production; these let a person watch a retry within seconds.
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1))
                .DefaultRetryDelay(TimeSpan.FromSeconds(4))
                .RetryBackoffMultiplier(1.0)
                .MaxRetryDelay(TimeSpan.FromSeconds(10))
                .ConfigureLocalWorkers(workers =>
                    workers.PollingInterval = TimeSpan.FromMilliseconds(250)
                )
                // Polling every second, the scheduler records two runs of its own each second,
                // about 170,000 a day. Sweep those; the scenario trains are not listed, so their
                // runs, junction runs and decisions stay for the page to read.
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = builder.Configuration.GetValue(
                        "Recovery:SchedulerRunRetention",
                        TimeSpan.FromMinutes(10)
                    );
                    cleanup.CleanupInterval = builder.Configuration.GetValue(
                        "Recovery:CleanupInterval",
                        TimeSpan.FromMinutes(1)
                    );
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

```json from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Api/appsettings.Development.json
  "Trax": {
    "Decisions": {
      // A demo state hash key (base64 of 32 bytes), for Development only. A real host keeps its
      // key with its other secrets, and every process that may run a retry uses the same one.
      "StateHashKey": "SWmkdRacZQMMBDRxWOVhVwA/qqEdGSnu68s04ayvzTw="
    }
  },
```

See [Keying the state hash](/docs/effect/decisions#keying-the-state-hash).

### Who sees what

The page signs in as a demo user and follows only its own runs, through
[`runProgress`](/docs/sdk-reference/graphql-api/run-progress). The refund and topic map trains carry
`[TraxRunProgress(Answers = true)]`, so the user who started a run sees each question's answer and the junctions on a
track; nobody else sees that run at all. The operations namespace stays behind its own role, for the dashboard and
an operator. The sample registers three demo keys in Development only:

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery.Api/Program.cs
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.User, id: "demo-user", RecoveryRoles.User)
            .Add(DemoKeys.Operator, id: "operator", RecoveryRoles.Operator, RecoveryRoles.User)
            .Add(DemoKeys.Viewer, id: "viewer", RecoveryRoles.Viewer)
    );
```

The page's trains (`startRun`, `decisionJournal`, `changeCaseData`, `orderTrouble`, `tabletCalls`) and the topic
map's `Build` and `Rebuild` require `recovery-user`. The refund train also carries `[TraxBroadcast]` and
`[TraxAuthorize(Roles = "Operator,Viewer")]`, so the viewer key follows every refund's steps through the broadcast
view, without answers. The topic map's train carries no `[TraxBroadcast]`: the `topic-map` machine invokes it, and a
user's own machine may not invoke one that broadcasts every run to every subscriber.

Outside Development no key exists, the operations namespace answers no one and the dashboard is
not mapped.

### The trains

Every step is an `EffectJunction`: a plain `Junction` emits no junction events. The refund asks the
model two questions, and stores its outcome before it emails the customer:

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
[TraxRunProgress(Answers = true)]
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

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Model/DemoDecider.cs
    public async Task<DecisionResult> Decide(
        DecisionRequest request,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(Latency(), cancellationToken);
        return Answer(request);
    }
// ...
        var answers = new Dictionary<string, Answer>();
        foreach (var question in request.Questions)
            answers[question.Key] = (request.State, question) switch
            {
                (RefundCase refund, ChoiceQuestion) => NameProblem(refund),
                (RefundEvidence evidence, YesNoQuestion) => ApproveRefund(evidence),
                (CoCitationEvidence evidence, YesNoQuestion) => SameTopic(evidence),
                (MatchEvidence evidence, YesNoQuestion) => SameWork(evidence),
                _ => throw new InvalidOperationException(
                    $"The demo model cannot answer {question.Key} about {request.State.GetType().Name}."
                ),
            };
        return new DecisionResult(answers);
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

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Trains/StartRun/StartRunTrain.cs
[TraxAuthorize(Roles = RecoveryRoles.User)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Starts a recovery demo run as a one-off manifest"
)]
public class StartRunTrain : ServiceTrain<StartRunInput, StartRunOutput>, IStartRunTrain
{
    protected override Task<Either<Exception, StartRunOutput>> Junctions() =>
        Chain<ScheduleDemoRun>().Resolve();
}
```

Its junction, for a refund:

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Trains/StartRun/Junctions/ScheduleDemoRun.cs
        return scheduler.ScheduleOnceAsync<IApproveRefundTrain, RefundInput, RefundResult>(
            externalId,
            new RefundInput { RunId = runId, OrderId = orderId },
            TimeSpan.Zero,
            // Every run of the manifest, each retry included, is the caller's to follow.
            options => options.MaxRetries(MaxRetries).StartedBy(caller.GetCurrentPrincipalId())
        );
```

`MaxRetries` is 2 in `ScheduleDemoRun`, which allows three attempts. `StartedBy` records the caller as the starter of
every run of the manifest, so the page follows each attempt through `runProgress`: the mutation answers the first
run's id, and each failed run names the retry that follows it (`retriedAs`).

### Crashing once without changing the input

A retry replays only when the manifest's input is byte-identical between attempts, so a "crash
here" flag cannot live in the input. `FaultInjector` is a singleton keyed by the run id the input
already carries. `startRun` arms it, and the crashing junction fires it once. In `IssuePayment`:

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Trains/Refund/Junctions/IssuePayment.cs
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

```csharp from=Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Trains/Topics/BuildTopicMapTrain.cs
        Chain<LoadCorpus>()
            .Parallel(signals =>
                signals
                    .Branch("embedding", b => b.Chain<EmbeddingSimilarity>())
                    .Branch(
                        "cocitation",
                        b =>
                            b.Chain<CountSharedReferences>()
                                .Gate<CoCitationEvidence, SameTopic>(gate =>
                                    gate.Yes(t => t.Chain<TrustCoCitation>(), atLeast: 0.8)
                                        .No(t => t.Chain<IgnoreCoCitation>(), below: 0.3)
                                        .Unsure(t => t.Chain<DampenCoCitation>())
                                )
                    )
                    .Branch("authors", b => b.Chain<AuthorOverlap>())
            )
            .Chain<CombineSignals>()
            .Chain<FindHiddenTwins>()
            .Resolve();
```

Each branch starts from a copy of Memory taken after `LoadCorpus`, runs in its own DI scope, and adds one
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

Three state machines run trains through [`Invokes`](/docs/statemachine/invoking-trains): one
system-owned `source-partition` instance per index partition ingests it, each user's `topic-map`
wizard builds a map in its `Building` state, and a user's `order` delivers food in its `Ordered`
state. [Recovery state machines](/docs/samples/recovery/state-machines) walks through all three.

### The page

The page is React 19, Vite, Apollo Client and `graphql-ws`, and sends every operation by a persisted
id. [How the Recovery page works](/docs/samples/recovery/the-page) covers what it reads and how it draws
each view.

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.E2E
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.Browser
```

`Trax.Samples.Recovery.E2E` boots the real host against Postgres and asserts over GraphQL;
`Trax.Samples.Recovery.Browser` drives the built page in chromium with Playwright for .NET.
[Recovery tests](/docs/samples/recovery/tests) lists what each test proves and what the browser tests
need.

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [Switch](/docs/sdk-reference/train-methods/switch) | [Gate](/docs/sdk-reference/train-methods/gate) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [Mutations](/docs/sdk-reference/graphql-api/mutations) | [Queries](/docs/sdk-reference/graphql-api/queries) | [Invoking a train](/docs/statemachine/invoking-trains) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances)
