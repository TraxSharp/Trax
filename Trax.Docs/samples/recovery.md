---
layout: default
title: Recovery
description: "The Recovery sample: a train asks a model, a later step crashes, and the manifest's retry replays the decisions while a page shows every junction live."
parent: Samples & Deployment
nav_order: 3
---

# Recovery

A train asks a model which track to take, a later step crashes, and the retry takes the same tracks
without paying for the model again. The Recovery sample makes that visible. Its page shows the
train's real C# with the running step highlighted, a console that narrates every junction event as
it arrives, and a timeline with one lane per attempt. Attempt 2's lane reads **replayed: model not
asked** for each question, and its question bars take milliseconds instead of the model's second or
so. Progress pills follow the run through **Runs**, **Breaks** and **Recovers**.

It proves three features working together, against Postgres, in one process:

| Feature | What the sample shows | Page |
|---|---|---|
| Train decisions | A `Gate` (refund approval), a `Switch` and a `Scale` (research brief), answered by an `IDecider` | [Decisions](/docs/core/decisions) |
| Retries replay decisions | The manifest's automatic retry replays every recorded answer whose state hashes the same, asks afresh when the data changed, and asks afresh on purpose with `askAfresh` | [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) |
| Junction events | `onJunctionEvent` and `operations.junctionRuns` drive the page | [Junction Events](/docs/effect/junction-events) |

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
   writing the report. The sidebar counts down to the retry. A few seconds later the manifest's
   retry starts as a new execution: `PlanResearch` runs again, but both questions arrive with
   `replayed: true` in a few milliseconds, the retry takes the same tracks, and the run completes.
2. **Refund, A-1001, Run.** Attempt 1 runs `LoadRefundCase`, asks `ApproveRefund` (yes at 0.93) and
   crashes in `IssuePayment`. The retry replays the answer and takes the same `Yes` track.
3. **Change the data during the backoff.** Pressed while the retry counts down, it records an
   earlier refund on the order. The retry is still queued to replay attempt 1, but the refund case
   it reads no longer hashes the same, so the replay is refused, the model is asked again (0.58,
   between the bars), and the refund takes the `Unsure` track to a person instead of being paid.
   The lane reads **asked afresh: state changed**.
4. **Ask afresh during the backoff.** It calls `triggerManifest(externalId, askAfresh: true)`, so
   the retry starts at once and asks afresh; the lane reads **asked afresh: on purpose**.
5. **Another track.** Orders A-1002 and A-1003 crash too: the model is unsure about A-1002 and
   declines A-1003, so their runs take the review and decline tracks, the step on that track crashes
   the same way, and the retry reuses the answer and takes the same track.
6. **Ask afresh after a run.** Once a run has completed, **Ask afresh** calls
   `requeueExecution(id, askAfresh: true)` on its last execution, a run of its own outside the
   manifest, shown as a `[requeue]` lane.

Case files and armed crashes live in memory. A run started before the host restarts has lost its
case file, so every retry fails and the manifest dead-letters. A requeue would fail the same way, so
the page offers no re-run once a run is dead.

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
    junctionRuns(metadataId: 42) { position kind name state answer replayed trackPosition attempt }
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

The two scenario trains carry `[TraxBroadcast]` and `[TraxAuthorize(Roles = "Operator,Viewer")]`,
so the viewer key follows their steps through the broadcast view. Once a token scheme is
registered, a subscription socket without a credential is refused at `connection_init`, so a
"public" watcher still needs a key. The alternative to an operator key is
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
            .Chain<Summarize>()
            .Resolve();
}
```

Every `Switch` track produces `Findings` and every `Scale` track `CheckedFindings`, because after a
routing step the chain can rely only on what every track produces. The refund approval asks one
yes/no question:

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

### Changing the data during the backoff

`changeCaseData` edits the order the next attempt will read, not the manifest's input. The retry is
therefore still linked to the failed run (`replay_decisions_of`), and the replay itself refuses the
answer: its recorded `state_hash` no longer matches the state the question is asked about now. The
refusal is stored as `replay_refused` in the new row's answer. The run is not marked
`replay_abandoned`, which is kept for a replay that could not be honoured at all (the named run gone,
a host that does not record).

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

## The tests

`Trax.Samples.Recovery.E2E` boots the real host with `WebApplicationFactory` against the
`recovery_e2e_tests` database (port 5432, or `TRAX_TEST_PG_PORT`), with a counting decider in place
of the demo one, and asserts over GraphQL:

| Test | Proves |
|---|---|
| `ResearchRun_CrashedOnce_CompletesOnAttempt2_WithoutAskingTheModelAgain` | Attempt 2 completes; the decider was asked each question once across both attempts; attempt 2's decision events have `replayed: true`, live and stored |
| `RefundRun_PaymentTimedOutOnce_RetryTakesTheSameTrackWithoutAskingAgain` | The gate replays its answer; the state hash is keyed (`k1:`) |
| `RefundRun_OnAnotherTrack_CrashesOnce_AndTheRetryReplaysTheDecision` | Orders A-1002 (review) and A-1003 (decline) crash once on their track, and the retry replays the answer |
| `RunWithNoCrashArmed_CompletesInOneAttempt` | One attempt, and the once manifest disables itself |
| `TriggerWithAskAfresh_DuringTheBackoff_RetryAsksTheModelAgain` | `triggerManifest(askAfresh: true)` makes the retry ask again |
| `RequeueExecution_ReplaysByDefault_AndAsksAgainWithAskAfresh` | `requeueExecution` replays; with `askAfresh: true` it asks again |
| `DataChangedDuringTheBackoff_RetryAsksAfresh_AndTakesTheTrackTheNewDataCallsFor` | The retry stays linked, refuses the answer for a changed state, asks afresh and takes `Unsure` |
| `ViewerSubscriber_SeesTheShape_ButNotTheAnswersOrTheTrack` | The broadcast view gets no answers, and every step on a track is `(withheld)` |
| `TheSchedulersOwnRuns_AreSweptAfterTheirRetention` | The scheduler's own runs are deleted once past their retention |

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.E2E
```

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [Switch](/docs/sdk-reference/train-methods/switch) | [Scale](/docs/sdk-reference/train-methods/scale) | [Gate](/docs/sdk-reference/train-methods/gate) | [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [Mutations](/docs/sdk-reference/graphql-api/mutations) | [Queries](/docs/sdk-reference/graphql-api/queries)
