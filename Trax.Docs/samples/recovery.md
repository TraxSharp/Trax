---
layout: default
title: Recovery
description: "The Recovery sample: a crashed run resumes without asking its model twice, a topic map runs signals in parallel, and state machines run trains."
parent: Samples & Deployment
nav_order: 3
---

# Recovery

A train asks a model which track to take, a later step crashes, and the retry takes the same tracks
without paying for the model again. The Recovery sample makes that visible. Its page shows the
train's real C# with the running step highlighted, a console that narrates every junction event as
it arrives, and a timeline with one lane per attempt. A refund's attempt 2 lane reads **replayed:
model not asked** for its question, and its question bar takes milliseconds instead of the model's
second or so. The research brief stores its checked findings in a checkpoint, so its attempt 2
resumes there and its lane holds only the report step. Progress pills follow the run through
**Runs**, **Breaks** and **Recovers**. A third scenario,
the topic map, runs three similarity signals side by side in parallel branches, and each lane draws
its attempt on the train's declared graph with the branches next to each other. Two state machines
run trains as well: one system-owned instance per partition of a scholarly index ingests it, and each
user builds their own topic map through a wizard whose `Building` state runs the map's train. A third,
a food order with no page of its own, is what traxsharp.net plays back.

It proves six features working together, against Postgres, in one process:

| Feature | What the sample shows | Page |
|---|---|---|
| Train decisions | A `Gate` (refund approval), a `Switch` and a `Scale` (research brief), answered by an `IDecider` | [Decisions](/docs/core/decisions) |
| Retries replay decisions | The manifest's automatic retry replays every recorded answer whose state hashes the same, asks afresh when the data changed, and asks afresh on purpose with `askAfresh` | [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) |
| Checkpoints and resume | The research brief checkpoints its checked findings; the manifest's retry resumes after it, and an operator resumes the failed run at `Summarize` with `resumeExecution` or the dashboard's **Resume** | [Resuming from a checkpoint](#resuming-from-a-checkpoint) |
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

1. **Research, crash once, Run.** Attempt 1 runs `PlanResearch`, asks `Source` (where to look) and
   `Depth` (how far to dig), runs a step on each chosen track, and crashes in `Summarize` while
   writing the report. Before it did, the run stored its `CheckedFindings` in a checkpoint. The
   sidebar counts down to the retry. A few seconds later the manifest's retry starts as a new
   execution and resumes after the checkpoint: its lane holds only `Summarize`, its run graph marks
   every step before the checkpoint **restored**, neither question is asked again, and the run
   completes.
2. **Refund, A-1001, Run.** Attempt 1 runs `LoadRefundCase`, asks `ApproveRefund` (yes at 0.93) and
   crashes in `IssuePayment`. The retry replays the answer and takes the same `Yes` track.
3. **Change the data during the backoff.** Pressed while the retry counts down, it records an
   earlier refund on the order. The retry is still queued to replay attempt 1, but the refund case
   it reads no longer hashes the same, so the replay is refused, the model is asked again (0.58,
   between the bars), and the refund takes the `Unsure` track to a person instead of being paid.
   The lane reads **asked afresh: state changed**.
4. **Ask afresh during the backoff.** On a refund it calls
   `triggerManifest(externalId, askAfresh: true)`, so the retry starts at once and asks afresh; the
   lane reads **asked afresh: on purpose**. A research run does not offer it: its retry resumes after
   both of its questions, so there is nothing left to ask.
5. **Another track.** Orders A-1002 and A-1003 crash too: the model is unsure about A-1002 and
   declines A-1003, so their runs take the review and decline tracks, the step on that track crashes
   the same way, and the retry reuses the answer and takes the same track.
6. **Ask afresh after a run.** Once a run has completed, **Ask afresh** calls
   `requeueExecution(id, askAfresh: true)` on its last execution, a run of its own outside the
   manifest, shown as a `[requeue]` lane. A requeue runs the chain from the top, so a research run
   asks both questions again.
7. **Resume from Summarize.** Once a research run is over, **Resume from Summarize** calls
   `resumeExecution(id, from: "Summarize#0")` on its failed attempt, the operation behind the
   dashboard's **Resume from here**. The `[resume]` lane runs only `Summarize`, from the findings the
   checkpoint stored, and writes the same report the retry did.

8. **Topic map, crash once, Run.** Attempt 1 runs `LoadCorpus`, then three branches at once:
   `embedding`, `cocitation` and `authors`. The `cocitation` branch counts shared references, asks
   `SameTopic` (do papers that cite the same works share a topic in this slice?), and crashes in the
   step on the track the answer picks. The run fails with a `BranchesFailedException` naming
   `Parallel#0/cocitation`, and `CombineSignals`, the join, never runs, so nothing is written. The
   retry replays the answer and completes. The lane's run graph shows the three branches side by
   side; the gate inside `cocitation` shows its three tracks, with the one taken marked.
   The three slices on the picker take the gate's three tracks with the demo model: 2016 to 2025
   trusts shared references (`Yes`, 0.87), 2021 to 2025 is unsure (0.52) and dampens them, 2022 to
   2025 ignores them (`No`, 0.28).

Case files and armed crashes live in memory. A run started before the host restarts has lost its
case file, so every retry that reads it again fails and the manifest dead-letters (a research run
that reached its checkpoint does not read it again). A requeue would fail the same way, so the page
offers no re-run once a run is dead.

The same over GraphQL, with the header `X-Api-Key: recovery-operator-key-do-not-use-in-production`:

```graphql
mutation {
  dispatch {
    startRun(input: { scenario: REFUND, orderId: "A-1001", crashOnce: true }) {
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

The topic map starts the same way, with `scenario: TOPIC_MAP` and, optionally, `fields`, `fromYear`
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

The research and refund trains carry `[TraxBroadcast]` and `[TraxAuthorize(Roles = "Operator,Viewer")]`,
so the viewer key follows their steps through the broadcast view. The topic map's train carries
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

Every step is an `EffectJunction`: a plain `Junction` emits no junction events. The research brief:

```csharp
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
public class ResearchTopicTrain : ServiceTrain<ResearchInput, ResearchReport>, IResearchTopicTrain
{
    protected override Task<Either<Exception, ResearchReport>> Junctions() =>
        Chain<PlanResearch>()
            .Switch<ResearchBrief, Source>(tracks =>
                tracks
                    .When(Source.Web, t => t.Chain<SearchWeb>())
                    .When(Source.Papers, t => t.Chain<SearchPapers>())
                    .When(Source.Wiki, t => t.Chain<SearchWiki>()))
            .Scale<Findings, Depth>(scale =>
                scale
                    .AtLeast(Depth.Skim, t => t.Chain<SkimSources>())
                    .AtLeast(Depth.CrossCheck, t => t.Chain<FetchFullTexts>()))
            .Checkpoint<CheckedFindings>()
            .Chain<Summarize>()
            .Resolve();
}
```

Every `Switch` track produces `Findings` and every `Scale` track `CheckedFindings`, because after a
routing step the chain can rely only on what every track produces. `CheckedFindings` is also all
`Summarize` reads, which is why the checkpoint stores it; see
[Resuming from a checkpoint](#resuming-from-a-checkpoint). The refund approval asks one yes/no
question:

```csharp
Chain<LoadRefundCase>()
    .Gate<RefundCase, ApproveRefund>(gate =>
        gate.Yes(t => t.Chain<IssuePayment>(), atLeast: 0.8)
            .No(t => t.Chain<DeclineRefund>(), below: 0.3)
            .Unsure(t => t.Chain<QueueForReview>()))
    .Chain<NotifyCustomer>()
    .Resolve();
```

A replay needs the state at each decision to hash exactly as it did the first time, so the states
hold only what the model reads and nothing that changes between attempts: no timestamp, no random
id, no cache. `LoadRefundCase` copies the order's amount, reason and earlier refunds into
`RefundCase`, because the hash covers the state and nothing the decider might look up elsewhere.

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
            (ResearchBrief brief, ChoiceQuestion) => ChooseSource(brief),   // ChoiceAnswer
            (Findings findings, ScoreQuestion) => ScoreDepth(findings),     // ScoreAnswer
            (RefundCase refund, YesNoQuestion) => ApproveRefund(refund),    // YesNoAnswer
            (CoCitationEvidence e, YesNoQuestion) => SameTopic(e),          // YesNoAnswer
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

A real model reads the case. Nimble paid A-1001 (0.99) and, once **Change the data during the
backoff** had added an earlier refund, was asked again and declined it (0.13), where the demo
decider sends it to review. Nimble's latest release ships no fitted probability temperature and is
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
already carries. `startRun` arms it, and the crashing junction fires it once:

```csharp
if (faults.TryFire(findings.RunId, CrashPoint.Report))
    throw new TimeoutException("The report store did not answer in time (crash injected by the demo).");
```

That is `Summarize`, the research brief's last step: every route reaches it, after both questions.

In the refund train the crash point is `CrashPoint.RefundTrack`. Each step the approval can route to
(`IssuePayment`, `DeclineRefund`, `QueueForReview`) fires it first, before doing its work, so the
crash lands on whichever track the gate picks and a retry never pays twice. `startRun` arms it only
after the manifest is scheduled.

Killing the worker process would not show a recovery: a killed run is failed by stuck-job recovery
much later, or at the next start, not resumed.

### Resuming from a checkpoint

Before the checkpoint, a research run has planned, asked the model twice, searched and checked what
it found. A crash in `Summarize` used to repeat all of it, because a retry runs the chain from the
top. `.Checkpoint<CheckedFindings>()` stores the `CheckedFindings` in Memory when the run reaches it,
with the track each routing step took, in `trax.checkpoint`. Nothing else is stored. The state must
come back from JSON as the same value, so `CheckedFindings` and the `Findings` inside it are sealed
records of data; the host refuses to start otherwise and names the member.

**A manifest's retry now resumes by itself.** When the failed run has a checkpoint, the retry is
queued to resume after it: it runs `Junctions()` again, skips every step before the checkpoint in
place, puts the stored `CheckedFindings` back in Memory and runs only `Summarize`. Neither question
is asked: both come before the checkpoint. That is also why the page offers no **Ask afresh** during
a research run's backoff. A dead letter's requeue resumes the same way; `requeueExecution` still
reruns from the top.

**What the operator sees.** On the failed run, `operations.runGraph` sets `canResume`, marks the
checkpoint's node `checkpointed` and every node after it that a resume can start at `canResume`;
nothing before the checkpoint can. The dashboard's Metadata Detail page offers **Resume** beside
**Re-queue**, puts a **checkpoint** badge on the stored step of its run graph and a **Resume from
here** on each step the graph allows; both call `IOperationsService.ResumeExecutionAsync`, the call
behind `resumeExecution`. On the resumed run, every node before its resume point is `RESTORED`: it
was skipped, not run. Neither surface ever shows what a checkpoint holds.

```graphql
query {
  operations {
    runGraph(metadataId: 42) { canResume nodes { id kind state canResume checkpointed } }
  }
}

mutation {
  operations {
    resumeExecution(id: 42, from: "Summarize#0") { success message id }
  }
}
```

`resumeExecution` resumes a failed or cancelled run, at the step `from` names or, without it, after
its latest checkpoint. It answers with the work queue entry's id, like a requeue, and the run is not
one of the manifest's executions. One resume of a run may be queued at a time: while the manifest's
retry is queued it already resumes the failed run, and a second resume is refused. The page offers
**Resume from Summarize** once the run is over for that reason. See
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

`OrderMachine` (`order`) is the smallest of the three, written to be read line by line beside its recording on
traxsharp.net (`scripts/recordings/order.mjs`). A user fills a cart (`addItem`, priced by the server through a custom
reduction) and sends `placeOrder`, which enters `Ordered`; `Ordered` invokes `DeliverOrderTrain`, whose outcome ends
the order in `Delivered` or `Cancelled`. A cart nobody orders is cleared by the server's timer after 30 minutes
(`Recovery:CartWait` shortens it for the recording). The train's only rule is a signed-in caller (a bare
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

The page is React 19, Vite, Apollo Client and `graphql-ws`, with subscriptions split onto a
`GraphQLWsLink` as in the Chat Service sample. The key travels in the
`connection_init` payload as `apiKey`, because a browser cannot set headers on a WebSocket upgrade.
For each attempt it:

1. polls `operations.executions(manifestId:)` until the attempt's execution exists;
2. subscribes to `onJunctionEvent(metadataId:)`;
3. then reads `operations.junctionRuns(metadataId:)` and merges both by `position`, keeping
   whichever copy of a step is further along, because the stored rows trail the stream and steps
   published before the subscription started reach it only through the table;
4. once the attempt ends, reads the decision journal.

Junction events say whether an answer was `replayed`, not why one was not. The sample adds a small
`[TraxQuery]` train, `decisionJournal(metadataId)`, that reads `IDataContext.RecordedDecisions` and
the run's `ReplayDecisionsOf` and `ReplayAbandoned`, so the page can tell **asked afresh: state
changed** (a `replay_refused` reason) from **asked afresh: on purpose** (no replay link). The journal
also carries each answer's `stateHash`.

A `ROUTE` step carries `replayed: false` even when the decision it routes on was replayed; the
timeline's badges read the question's step.

The code panel imports the trains' `.cs` files raw at build time and highlights the line of the
running step: `Chain<Name>` for a junction, the routing step for a question, and the track's
`.When(...)`, `.AtLeast(...)` or `.Yes(...)` for a route. It shows the chain, from `Junctions()` to
`Resolve()`, and sizes its font so the longest line fits unwrapped, so the code stays still while the
highlight moves, and it shows the train the picker selects. The timeline gives each attempt a lane on
one time axis: a junction's bar spans its run, a question's bar the time the answer took to arrive.
A step that ran in a branch is labelled with the branch.

Under each lane the page draws the attempt's run graph from `operations.runGraph(metadataId:)`,
polled while the attempt runs and read once more when it ends. Each declared step is a node in the
order the chain declares it, coloured by its state (completed, failed, running, not reached,
skipped, interrupted), with the state also given as text for screen readers. A routing step shows
its tracks next to each other with the one taken marked, and a `Parallel` step shows its branches
as columns side by side, since they ran at the same time. The page reads the graph as `allNodes`,
one flat list at any depth, and rebuilds the tree from each node's `parentId` and `track`, so it
draws a chain nested deeper than a query could follow nested tracks. Each read is numbered and an
answer older than one already drawn is dropped, and a poll waits for the one before it, so a slow
answer never draws an older graph over the final one.

A run that resumed records only the steps after its resume point, so its lane holds those alone and
its graph draws the rest as restored, with a badge counting them. **Resume from Summarize** sends
`resumeExecution` with the node id `Summarize#0` for the latest failed research attempt, then reads
the work queue entry it answers with until the entry names its run, and follows that run in a
`[resume]` lane, as **Ask afresh** follows a requeue.

## The tests

`Trax.Samples.Recovery.E2E` boots the real host with `WebApplicationFactory` against the
`recovery_e2e_tests` database (port 5432, or `TRAX_TEST_PG_PORT`), with a counting decider in place
of the demo one, and asserts over GraphQL. `RestartTests` starts the host as two processes of its own, one after
the other, on the `recovery_restart_e2e_tests` database, so it can kill the first the way a crash does:

| Test | Proves |
|---|---|
| `ResearchRun_CrashedOnce_CompletesOnAttempt2_WithoutAskingTheModelAgain` | Attempt 2 resumes after the checkpoint and completes; its junction events, live and stored, hold only `Summarize`; the decider was asked each question once across both attempts |
| `A_crash_in_Summarize_resumes_from_CheckedFindings_without_fetching_again` | The failed run's graph offers `Summarize#0`; `resumeExecution` and the dashboard's resume each run only `Summarize`, write the retry's report and draw the steps before as `RESTORED`; `TraxInvariants` finds nothing new |
| `RefundRun_PaymentTimedOutOnce_RetryTakesTheSameTrackWithoutAskingAgain` | The gate replays its answer; the state hash is keyed (`k1:`) |
| `RefundRun_OnAnotherTrack_CrashesOnce_AndTheRetryReplaysTheDecision` | Orders A-1002 (review) and A-1003 (decline) crash once on their track, and the retry replays the answer |
| `RunWithNoCrashArmed_CompletesInOneAttempt` | One attempt, and the once manifest disables itself |
| `TriggerWithAskAfresh_DuringTheBackoff_RetryAsksTheModelAgain` | `triggerManifest(askAfresh: true)` makes a refund's retry ask again |
| `RequeueExecution_ReplaysByDefault_AndAsksAgainWithAskAfresh` | `requeueExecution` replays; with `askAfresh: true` it asks again |
| `DataChangedDuringTheBackoff_RetryAsksAfresh_AndTakesTheTrackTheNewDataCallsFor` | The retry stays linked, refuses the answer for a changed state, asks afresh and takes `Unsure` |
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

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [Switch](/docs/sdk-reference/train-methods/switch) | [Scale](/docs/sdk-reference/train-methods/scale) | [Gate](/docs/sdk-reference/train-methods/gate) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [Mutations](/docs/sdk-reference/graphql-api/mutations) | [Queries](/docs/sdk-reference/graphql-api/queries) | [Invoking a train](/docs/statemachine/invoking-trains) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances)
