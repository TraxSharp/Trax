---
title: Decision records
description: "What Trax.Effect records for each decision a run makes, the trax.decision columns, the System One adapter, and how re-queued and retried runs replay answers."
type: reference
area: effects
nav_order: 8
source:
  - Trax.Effect/src/Trax.Effect.Data/Decisions
  - Trax.Effect/src/Trax.Effect/Models/RecordedDecision/RecordedDecision.cs
  - Trax.Effect/src/Trax.Effect.Decisions.SystemOne
---

# Decision records

A train's [decisions](/docs/core/decisions) run without Trax.Effect; Trax.Core needs only an
`IDecider`. Trax.Effect adds three things around them: an adapter for typed decision models, a
record of every decision against the run that made it, and re-queued and retried runs that take
the tracks the original took. What a decision is, how a replay is checked and how the state is
hashed are on [Decisions](/docs/core/decisions).

```csharp compile
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Decisions.SystemOne.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording()
    .AddNimbleDecider(o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))));
```

| Call | Package | `using` |
|---|---|---|
| `AddDecisionRecording` | `Trax.Effect.Data` (comes with any data provider package) | `Trax.Effect.Data.Extensions` |
| `AddJunctionEvents` | `Trax.Effect.Data` | `Trax.Effect.Data.Extensions` |
| `UsePostgres` | `Trax.Effect.Data.Postgres` | `Trax.Effect.Data.Postgres.Extensions` |
| `AddNimbleDecider`, `AddSystemOneDecider` | `Trax.Effect.Decisions.SystemOne` | `Trax.Effect.Decisions.SystemOne.Extensions` |
| `IDecider`, `StateHashKey`, `RuleDecider`, `ScriptedDecider`, `[Asks]` | `Trax.Core` | `Trax.Core.Decisions` |

[Answer decisions with Nimble](/docs/effect/answer-decisions-with-nimble) sets up a model, and
[Recover a deciding train without asking the model again](/docs/effect/recover-a-deciding-train)
lists what a train needs for its retries to replay.

## Typed decision models

### Nimble

`AddNimbleDecider` registers the System One adapter (below) with the model name and limits of
[Nimble](https://docs.bespokelabs.ai/nimble/overview), Bespoke Labs' open-weights typed decision
model. It talks to a Nimble server started from Nimble's own serving code, which answers
`POST /v1/systemone`. There is no default endpoint, and a host that registers Nimble without an
`Endpoint` fails to start. Its settings and defaults are on
[AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider).

The request cannot pin a model revision: the server serves whichever revision it was deployed
with. Token and body limits (8,192 prompt tokens a question, a 2 MiB body) cannot be checked
before sending, so a request over them is answered 413 or 422 and fails as `Permanent`.

### Other typed decision models

`Trax.Effect.Decisions.SystemOne` answers a train's questions through the **System One** request
format, which Jev introduced and Nimble's server accepts: one `POST` carrying the model, the state,
and the questions, each with its [question key](/docs/core/decisions#question-keys) as its id,
answered with one typed answer per question and the model's name. That name is the one the request
asked for, echoed back, not a version the server confirms. `AddNimbleDecider` is this adapter with
Nimble's model name and limits filled in; `AddSystemOneDecider` is the same adapter for Jev or
another server that accepts the format.

| Trax question | Sent as | Criteria sent | Read back |
|---|---|---|---|
| Choice | `choice` | each option's name and description | `choice`, `confidence` (or, without one, the chosen option's probability), `probabilities` |
| Score | `score` | each level's description, lowest first | `score`, `confidence` (or, without one, the nearest level's probability), `probabilities` keyed by every level index from 0 |
| Yes/no | `noul` | what yes and no mean (default `Yes` and `No`) | `noul`, the probability of yes |

The state is sent as a string when it is one, and otherwise as JSON with camel-cased names, so a
question's instructions can name a field.

Both methods refuse, at startup, a missing endpoint or one that is not an `http` or `https` URL,
a model not pinned to a version (`jev-latest`, a bare `jev`, `nimble-latest`; `AllowFloatingModel`
turns this off for `AddSystemOneDecider`), and plain HTTP to anything but a loopback address. A
blank API key sends no `Authorization` header.

`SystemOneDecider` implements [`IVetsQuestions`](/docs/core/decisions#deciders), so the startup
check refuses a declaration with more questions in one `Decide` than `MaxQuestions`, a question
with blank instructions, fewer than two or more than `MaxOptions` options or levels, an option
named twice, a kind of question the format cannot ask, or a state type JSON writes as a bare
number, `true` or `false`, and the host does not start with it.

Which failures are retried, and how each is classified, is the table under
[AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider#failures). A
failure's message gives the status, a few words on what it means, and the provider's request id
when the response carries one in its `x-typesafe-request-id` header. It never quotes the response
body, because the message is stored as the run's failure reason and a server's validation error
can echo the request, which carries the train's state. An answer the adapter cannot read is left
out, and the train fails on the unanswered question, `Transient`.

### Putting the model in front of a larger one

Without a name, each method registers the decider every train asks, as both `IDecider` and
`SystemOneDecider`, and may be called once; a second unnamed registration fails at startup instead
of replacing the first. With a name, it registers a keyed `SystemOneDecider` and nothing else, so
several models can sit side by side and be composed into a `CascadingDecider` registered as the
`IDecider`
([Escalating what the fast decider is unsure of](/docs/core/decisions#escalating-what-the-fast-decider-is-unsure-of)).
A keyed decider is not something `DecidedBy<TDecider>()` can name, because that resolves by type.

## Recording decisions

`AddDecisionRecording()` records, for every question a run asks, the question as asked, the answer
it acted on, the model and decider that gave it, any shadow's answer and whether it agreed, and
every track taken on it. Each decision is logged and written to `trax.decision` against the run's
metadata as it is made, before the train acts on it, through a short-lived data context of its own
rather than the run's. A run that fails, times out or is killed after deciding still shows what it
decided, and so does a step that failed on a decider's answer: a missing or unfit live answer is
recorded with the reason in `refused`. Each track a routing step takes is added to that question's
row when it is taken; more than one step can route on one decision (a `Decide` followed by two
`Switch` steps on the same choice), so the row keeps every routing in order rather than only the
last.

Recording is required, not best effort. A decision that cannot be written fails its step before
any track is taken, classified `Transient` (or `Permanent` when the store refuses the value),
because a re-queue of that run would otherwise have nothing to replay. A shadow's answer can never
cost the live record: a number JSON cannot hold is written as the string `"NaN"`, `"Infinity"` or
`"-Infinity"`, and an answer of a type the journal has no stored form for is recorded with the
reason in the shadow's error. A live answer of such a type fails its step, `Permanent`, since it
could never be read back. So does a decision the run's own train reports under an external id
other than the run's (the train changed its `ExternalId` while running). Decisions are matched to
the run through its async flow; when code in the run lost that flow (it suppressed
`ExecutionContext` flow, say), the journal looks the run up by its external id among the runs of
that train in progress on this host, and records against it when exactly one matches. Otherwise
the decision is logged only. Calling `AddDecisionRecording()` more than once registers it once.

It needs a data provider: before one it is a compile error. Each answer is stored with the hash of
the state it was given about ([`StateHash`](/docs/core/decisions#a-replay-matches-the-state)),
never the state itself. `AddDecisionRecording(o => o.ReplayAnswersFor(...))` sets how long a
recorded answer may be replayed; see
[Only into the same state, and only while fresh](#only-into-the-same-state-and-only-while-fresh).

### Keying the state hash

How the hash is taken and why it can be keyed is on
[Keying the state hash](/docs/core/decisions#keying-the-state-hash). `AddDecisionRecording` takes
the key from the first of these that is present:

| Source | How |
|---|---|
| A [`StateHashKey`](/docs/core/decisions#keying-the-state-hash) the host registered in the container itself | Kept as it is |
| `HashStatesWith(byte[] key)` | `AddDecisionRecording(o => o.HashStatesWith(key))`, at least 32 bytes |
| Configuration | Base64 of at least 32 bytes under `Trax:Decisions:StateHashKey` (`DecisionRecordingOptions.StateHashKeyConfigurationKey`) |

```csharp compile
effects.UsePostgres(connectionString)
    .AddDecisionRecording(o => o.HashStatesWith(Convert.FromBase64String(secret)));
```

A configured value that is not base64 or is shorter than 32 bytes is not ignored: the state is not
hashed at all, so nothing replays, rather than falling back to an unkeyed hash.

Without a key the hash is a plain SHA-256 (`s1:`), and the journal stores it only where that is
harmless. For a question whose state type can reach a member marked `[TraxSensitive]` (see
[On a member of a decision's state](/docs/sdk-reference/attributes/trax-sensitive#on-a-member-of-a-decisions-state)), a question
about a sensitive type, or a decision reported without its state type, the row is written with no
`state_hash`, so its answer is never replayed, and a warning naming the state type is logged once.

A question about a type marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type)
is recorded in full here, because a replay reads its answer from this table. The mark withholds the
answer from [junction events](/docs/effect/junction-events) and `trax.junction_run`, and the
journal's log line writes it as withheld, along with why a recorded answer to it was not replayed,
but `trax.decision` keeps it. After a route on such a question, the log also withholds the question
key, answer, track and decider of every later decision, refusal and route of the run. Which
questions are sensitive is decided by the type the question is about (`DecisionMade.QuestionType`),
with inheritance, so a subclass of a marked type is withheld even when no scan saw it.

### The decision table

`trax.decision` is read through `IDataContext.RecordedDecisions` (entity `RecordedDecision`,
namespace `Trax.Effect.Models.RecordedDecision`). It ships in the core migration set of the
Postgres and Sqlite providers. A row is unique by `metadata_id`, `branch_path`, `question_key` and
`occurrence`.

| Column | Property | Holds |
|---|---|---|
| `id` | `Id` | The row's id |
| `metadata_id` | `MetadataId` | The run. Rows are deleted with it. |
| `question_key` | `QuestionKey` | The [question key](/docs/core/decisions#question-keys): the type's name without its namespace, or the `Key` set on `[Asks]` |
| `branch_path` | `BranchPath` | The `Parallel` branch the question was asked in, as Trax.Core names it (`Parallel#0/cocitation`), or empty outside any branch |
| `occurrence` | `Occurrence` | Which asking of the question this was in the run, from 0. Inside a branch it counts on from where the branch forked. |
| `fingerprint` | `Fingerprint` | The [fingerprint](/docs/core/decisions#observing-and-replaying) of the asking the answer was given to, 64 lowercase hex characters. A replay hands it back, and an answer whose fingerprint differs from the question as it is asked now is not replayed. |
| `kind` | `Kind` | `choice`, `score` or `yes_no` |
| `question` | `Question` | The question's instructions and criteria (jsonb) |
| `answer` | `Answer` | The answer acted on (jsonb), with `replay_refused` when an earlier run's answer was not replayed and the decider was asked afresh. On a refused row, the answer the run would not act on, or null when the decider gave none. |
| `refused` | `Refused` | Why the run would not act on the decider's answer, or null for an answer it acted on. Every row has an `answer` or a `refused` (a check constraint holds it). A refused row is never replayed. |
| `model` | `Model` | The model that answered, as the decider names it, or null for a decider that is not a model. For a System One model it is the name the request asked for, echoed back. |
| `decider` | `Decider` | The decider's type, or null for a replayed answer |
| `replayed` | `Replayed` | Whether the answer came from an earlier run |
| `shadows` | `Shadows` | Each shadow's answer, whether it agreed, and why it gave none (jsonb) |
| `routes` | `Routes` | Every track a routing step took on this decision, in order, as a jsonb array of `{"track": ..., "fallback_reason": ...}`; `fallback_reason` says why the decision was not followed, and is null when it was. Null when nothing routed on it. |
| `state_hash` | `StateHash` | The hash of the state the question was asked about: `k1:` and the hex of an HMAC-SHA256 under the host's key, or `s1:` and the hex of a SHA-256 without one. Null when the state could not be hashed, when no key is configured and the state can hold a `[TraxSensitive]` member, or when the row predates the column. A row with no hash is never replayed. |
| `decided_at` | `DecidedAt` | When it was answered |

The same migrations add to `trax.metadata` and `trax.work_queue`:

| Column | Table | Holds |
|---|---|---|
| `decisions_recorded` | `trax.metadata` | Set on a run's first write when the host records decisions, before any junction, so a replay can tell a run that reached no questions from one whose decisions were never recorded |
| `replay_decisions_of` | `trax.metadata`, `trax.work_queue` | The run whose decisions this run replays. Not a foreign key: the original may be deleted before the requeue runs. |
| `replay_abandoned` | `trax.metadata` | Set on a manifest's retry that named a run to replay and could not honour it; see [A requeue of a requeue](#a-requeue-of-a-requeue) |

### Other decision observers

Trax.Core takes one `IDecisionObserver` from the container. When Trax adds an observer of its own
(`AddDecisionRecording`, `AddJunctionEvents`), it registers a composite in its place that tells
every observer: each one Trax adds, and each one the host registered as an `IDecisionObserver`
before `AddTrax`. Required observers are told first, so a decision that could not be recorded is
never reported as made, then the best-effort ones.

An observer registered after `AddTrax` replaces the composite in the container, so decision
recording would never be told about a decision, and junction events would not hear the routings
they withhold tracks by. While decision recording or junction events are registered, such a host
refuses to start with an `InvalidOperationException` naming the observer, and every run refuses
too, for a host built without the generic host. Decorating `IDecisionObserver` (Scrutor's
`Decorate`, say) replaces the composite the same way and is refused the same way.

Each observer in the composite is built on its own. A host observer that is best effort as
registered (an instance that is not `Required`, or a type that leaves `Required` to its default)
and cannot be built is left out with a warning. Any other observer that cannot be built, including
one registered through a factory, fails every decision step and the host's start, with what
building it threw.

```csharp compile
using Trax.Core.Decisions;

services.AddSingleton<IDecisionObserver, DecisionAuditor>();   // before AddTrax: told alongside Trax's own
services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording()));
```

## Re-queued and retried runs replay their decisions

Re-queueing an execution, with the dashboard's **Re-queue** button or the
[`requeueExecution`](/docs/sdk-reference/graphql-api/mutations/executions#requeueexecution) mutation, queues a
run that replays the original's recorded decisions. Both go through
[`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync),
which sets the new run's `ReplayDecisionsOf` to the original's id when the original has decisions
to replay: it recorded a decision it acted on, or was itself queued to replay another run. A run of
a train that never decides is re-queued as an ordinary enqueue. A run's answers are replayed once:
when a queued entry or another run already replays the run being re-queued, the requeue asks afresh
and its message says so. **Re-queue, Ask Afresh** and `requeueExecution(askAfresh: true)` ask
afresh on purpose. Each question the new run asks is answered from what was recorded for the same
question and asking, without calling a decider or its shadows, and recorded with `replayed` set.
`Trax.Docs/adr/0041` records why a re-queue replays rather than asks again.

A manifest's automatic retry, and a requeue of its dead letter, replay the failed run's decisions
too, at most once in a row and only when the scheduler can show the answers were given by that
manifest's own run, about the same input. See
[Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions).
Queueing a train through `queueTrain` or `QueueTrainAsync` never replays, and neither does a
manifest's scheduled run that is not a retry, nor a train run directly.

### Only into the same state, and only while fresh

A recorded answer is replayed only when both hold:

- **The state hashes the same.** The question is asked about the state as it is in the repeated
  run, and Trax.Core replays the answer only when that state's hash equals the `state_hash` the
  answer was recorded with. See
  [A replay matches the state](/docs/core/decisions#a-replay-matches-the-state).
- **The answer is fresh.** It is younger than `ReplayAnswersFor`, 24 hours by default, counted from
  when a decider gave it. A requeue of a requeue that replayed the answer records a new row for it,
  but the age still counts from the decider's row, so replaying an answer never makes it younger. A
  replayed row whose answering run is no longer in the chain cannot be dated and is asked afresh.

```csharp compile
effects.UsePostgres(connectionString)
    .AddDecisionRecording(o => o.ReplayAnswersFor(TimeSpan.FromHours(6)));
```

`ReplayAnswersFor` takes at least one second; `TimeSpan.MaxValue`, or any span longer than the
calendar goes back, means no bound. An answer outside either bound is asked afresh, and that is
never a failure. A changed state is reported in the new row's `replay_refused`; an aged-out answer
is left out of the replay and logged at `Information`. Rows written before `state_hash` existed
have none, so the first requeue after upgrading asks afresh. Both bounds apply to every path that
names a run to replay: a manual requeue, a dead letter's requeue and a manifest's retry.
`Trax.Effect/docs/adr/0020` records why.

### A requeue of a requeue

A replay follows `replay_decisions_of` back through every run it repeats. For each question and
occurrence the nearest run's recorded answer wins, since that is what the nearest run acted on,
whether it replayed it or was answered afresh. A question that run never reached falls back to the
run it replayed, and so on. So a requeue of a requeue that failed before reaching a question still
takes the track the first run took there. The chain is followed at most 32 runs back.

A manifest's retry that named a run to replay and asked afresh because the replay could not be
honoured (below) is marked `replay_abandoned`, on its first write. It never acted on the answers of
the run it named, so a replay of it stops there: it replays that run's own recorded answers, and
fails, as for any run that did not record its decisions, when it recorded none. A run that names a
run to replay without the mark (one that never started, say) is passed through to the run it
names.

All of this is loaded once, before the run's first junction, so answering a question never waits
on the database. Metadata cleanup does not delete a run while a `Queued` work queue entry or a run
it keeps names it in `replay_decisions_of`, and does not delete a run that replays another while it
keeps the run it replays. When both have expired they are deleted together, in one batch. See
[Metadata cleanup](/docs/scheduler/admin-trains/metadata-cleanup).

| The replay meets | What happens |
|---|---|
| A question no run in the chain reached (they failed earlier, or the chain changed since) | Asked afresh |
| A recorded answer whose [fingerprint](/docs/core/decisions#observing-and-replaying) differs from the question as it is asked now, or that no longer fits it (an option renamed or removed, a scale with fewer levels, another kind of question) | Asked afresh, with the reason stored as `replay_refused` |
| A recorded answer given about a state that hashes differently from the state asked about now, or recorded with no `state_hash` | Asked afresh, with the reason stored as `replay_refused` |
| A recorded answer older than `ReplayAnswersFor`, counted from when a decider gave it | Asked afresh, and logged |
| A recorded choice of a member the switch has no track for | Replayed; it takes the `Otherwise` track again, as it did the first time |
| A refused row (the step failed on the decider's answer) | Skipped: never replayed, so the question is asked afresh |
| A host that does not call `AddDecisionRecording()` | The run fails before its first junction, `Permanent` |
| A run in the chain that no longer exists, or is a run of another train | The run fails before its first junction, `Permanent` |
| A run in the chain that ran without recording its decisions (`decisions_recorded` false, and it replayed nothing itself) | The run fails before its first junction, `Permanent`: what it decided cannot be known |
| A chain that leads back on itself, or goes back more than 32 runs | The run fails before its first junction, `Permanent` |
| A recorded answer that cannot be read | The run fails before its first junction, `Permanent` |
| A database failure while loading the chain | The run fails before its first junction, `Transient` |

A run that cannot honour its replay fails instead of asking afresh, because it was queued to
repeat the original.

A manifest's retry, or a requeue of its dead letter, is the exception. The scheduler queued it, not
someone who asked for the original's decisions, so when its replay cannot be honoured (the run it
names is gone or belongs to another train, the host does not record decisions, or a recorded answer
cannot be read) it logs a warning, asks afresh and is marked `replay_abandoned`. A manual requeue
still fails, `Permanent`. A run in the chain that recorded its decisions but reached no questions is
not a failure: there is nothing of its own to repeat, and the replay goes on to the run before it.

### Building a train that recovers

Every piece a train needs for a crash to be recovered without asking the model again is listed on
[Recover a deciding train without asking the model again](/docs/effect/recover-a-deciding-train).

### A host that does not record

A requeue is linked only when the run has decisions to replay, so a replay reaches a host without
`AddDecisionRecording()` when one host recorded the run and another runs the requeue: one worker of
a fleet left without the call. Every host that runs trains (`AddScheduler`, `AddTraxJobRunner`,
and `AddTraxWorker` through it) checks for this at startup. When `IDecisionReplay` is not
registered and some registered train's chain, or a track in it, asks a decider, the host refuses
to start: an `InvalidOperationException` lists those trains and says to call
`AddDecisionRecording()`. The check runs as the host starts, before any worker claims work.

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) | [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) | [TrainExecution](/docs/sdk-reference/mediator-api/i-train-execution-service)
