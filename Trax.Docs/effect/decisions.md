---
layout: default
title: Decision Recording and Models
description: How Trax.Effect records each decision against its run, replays them when a run is re-queued or retried, and answers questions with Nimble or another model.
parent: Effect
nav_order: 8
---

# Decision Recording and Models

A train's [decisions](/docs/core/decisions) run without Trax.Effect; Trax.Core needs only an
`IDecider`. Trax.Effect adds three things around them: an adapter for typed decision models, a
record of every decision against the run that made it, and re-queued and retried runs that take
the tracks the original took.

```csharp
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

A train that should recover from a crash without asking its model again needs more than the
recording; [Building a train that recovers](#building-a-train-that-recovers) lists every piece.

## Nimble

[Nimble](https://docs.bespokelabs.ai/nimble/overview) is Bespoke Labs' typed decision model, and
the one Trax builds around: its 9B weights are open (published under Apache 2.0, a fine-tune of
Qwen3.5-9B), so a train's decisions can run on hardware you own, with no data leaving it and no
per-request price.

`AddNimbleDecider` talks to a Nimble server you run, started from Nimble's own serving code
(`nimble/serving/server.py` in `bespokelabsai/nimble`), which answers `POST /v1/systemone`. There
is no default endpoint. Nimble's documentation describes no production hosted API, and the demo
it links to runs on one GPU and now refuses requests without Modal credentials, so it is not
somewhere to send a train's state and Trax does not point at it. A host that registers Nimble
without an `Endpoint` fails to start.

That server runs SGLang and needs an NVIDIA GPU. On a Mac with Apple Silicon, the Recovery sample
serves the same model through Nimble's MLX scorer instead; see
[Nimble on a Mac](/docs/samples/recovery#nimble-on-a-mac).

| Setting | Default | Why |
|---|---|---|
| `Endpoint` | none, required | The full URL of `POST /v1/systemone` on your server |
| `Model` | `bespokelabs/Bespoke-Nimble-9B` | The checkpoint id the server answers to. `nimble-latest` is refused. |
| `ApiKey` | none | Sent as a bearer token only when set. The server checks one only when it is started with `OPENJEV_API_KEY`. |
| `MaxConcurrentRequests` | 4 | One server container runs four evaluations at once and turns a fifth away with a 529. Raise it when the server scales to more containers. |
| `MaxOptions` | 26 | The most options or levels the server accepts on one question |
| Questions per request | 64 | The server's limit; a `Decide` asking more is refused at startup |
| `AttemptTimeout` | 30 seconds | A self-hosted server can be slow on its first request after loading |

The request cannot pin a model revision: the server serves whichever revision it was deployed
with. Pin the revision where you deploy the server, and re-check your confidence bars when you
change it. Token and body limits (8,192 prompt tokens a question, a 2 MiB body) cannot be checked
before sending, so a request over them is answered 413 or 422 and fails as `Permanent`.

On Bespoke's benchmarks Nimble is slightly less accurate than Jev (90.1% against 93.2% on their
held-out set); put it in front of a larger model with a
[cascade](#putting-the-model-in-front-of-a-larger-one) when that matters, and check its
confidence bars against your own labelled cases either way.

## Other typed decision models

`Trax.Effect.Decisions.SystemOne` answers a train's questions through the **System One** request
format, which Jev introduced and Nimble's server accepts: one `POST` carrying the model, the state,
and the questions, each with its [question key](/docs/core/decisions#question-keys) as its id,
answered with one typed answer per question and the model's name. That name is the one the request
asked for, echoed back, not a version the server confirms, so pin the version where the model is
deployed. `AddNimbleDecider` is this adapter with Nimble's model name and limits filled in;
`AddSystemOneDecider` is the same adapter for Jev or another server that accepts the format, so
moving between them is a change of endpoint and model name:

```csharp
effects.AddSystemOneDecider(o =>
{
    o.Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
    o.Model = "jev-1.13.0";
    o.ApiKey = configuration["Jev:ApiKey"];
});
```

| Trax question | Sent as | Criteria sent | Read back |
|---|---|---|---|
| Choice | `choice` | each option's name and description | `choice`, `confidence` (or, without one, the chosen option's probability), `probabilities` |
| Score | `score` | each level's description, lowest first | `score`, `confidence` (or, without one, the nearest level's probability), `probabilities` keyed by every level index from 0 |
| Yes/no | `noul` | what yes and no mean (default `Yes` and `No`) | `noul`, the probability of yes |

The state is sent as a string when it is one, and otherwise as JSON with camel-cased names, so a
question's instructions can name a field. Send a state that holds only what the questions need:
these models answer less accurately with detail that does not bear on the question.

[AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) and
[AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) refuse, at
startup, settings that would make decisions hard to trust or leak data:

- **There must be an endpoint**, an `http` or `https` URL. Nimble has no default to fall back on.
- **The model must be pinned to a version** (`jev-1.13.0`, not `jev-latest` or a bare `jev`;
  Nimble's `bespokelabs/Bespoke-Nimble-9B`, not `nimble-latest`). A confidence bar tuned against
  one version does not carry over to the next. `AllowFloatingModel` turns this off for
  `AddSystemOneDecider`.
- **The endpoint must be HTTPS** unless it is a loopback address, where a self-hosted model
  usually runs. The request carries the train's state and the API key.

The API key is optional; a blank one, such as an unset configuration value, sends no
`Authorization` header.

A throttled, unavailable or slow model, a connection that is refused, reset or times out, or a
`200` whose body is not a System One response (including one with no `answers` object, or one in a
character set that cannot be decoded) is retried with a doubling, jittered wait that stops growing
at `MaxRetryDelay` (30 seconds by default). A
`Retry-After` is honoured up to `MaxRetryDelay`; when the model asks for longer, or the retries run
out, the failure is classified `Transient`.

These are classified `Permanent` and not retried, because only a change to the request or the
configuration cures them:

- a request the model refuses: bad criteria, a bad key, a `501` or `505`
- a redirect. The adapter does not follow redirects; the endpoint is the one configured.
- an endpoint that cannot be reached as configured: its name does not resolve, its TLS handshake
  fails, or a proxy refuses the credentials
- a request that cannot be sent as it is: a state written as JSON `null`, or one that cannot be
  serialized at all. These depend on the value, so they are refused when the request is about to
  be sent.

What the model refuses whatever the state is refused earlier, at startup. `SystemOneDecider`
implements [`IVetsQuestions`](/docs/core/decisions#deciders), so the startup check refuses a
declaration with more questions in one `Decide` than `MaxQuestions`, a question with blank
instructions, fewer than two or more than `MaxOptions` options or levels, an option named twice, a
kind of question the format cannot ask, or a state type JSON writes as a bare number, `true` or
`false`, and the host does not start with it.

A failure's message gives the status, a few words on what it means, and the provider's request id
when the response carries one in its `x-typesafe-request-id` header. It never quotes the response
body, because a server's validation error can echo the request, and the request carries the
train's state, while the message is stored as the run's failure reason. Cancelling the run cancels
the request. An answer the adapter cannot read is left out, and the train fails on the unanswered
question, `Transient`, rather than acting on a guess.

### Putting the model in front of a larger one

Without a name, each method registers the decider every train asks, as both `IDecider` and
`SystemOneDecider`, and may be called once; a second unnamed registration fails at startup instead
of replacing the first. With a name, it registers a keyed `SystemOneDecider` and nothing else, so
several models can sit side by side. Compose them yourself, and register the result as the
`IDecider`:

```csharp
services.AddTrax(trax => trax.AddEffects(effects => effects
    .AddNimbleDecider("nimble", o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))
    .AddSystemOneDecider("jev", o =>
    {
        o.Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
        o.Model = "jev-1.13.0";
        o.ApiKey = configuration["Jev:ApiKey"];
    })));

services.AddSingleton<IDecider>(sp => new CascadingDecider(
    sp.GetRequiredKeyedService<SystemOneDecider>("nimble"),
    sp.GetRequiredKeyedService<SystemOneDecider>("jev"),
    escalateBelow: 0.8));
```

A keyed decider is not something `DecidedBy<TDecider>()` can name, because that resolves by type.
To give one step a different decider, register a type of your own for it.

See [Escalating what the fast decider is unsure of](/docs/core/decisions#escalating-what-the-fast-decider-is-unsure-of).

## Recording decisions

`AddDecisionRecording()` records, for every question a run asks, the question as asked, the answer
it acted on, the model and decider that gave it, any shadow's answer and whether it agreed, and
every track taken on it. Each decision is logged and written to `trax.decision` against the run's
metadata as it is made, before the train acts on it, through a short-lived data context of its own
rather than the run's. A run that fails, times out or is killed after deciding still shows what it
decided, and so does a step that failed on a decider's answer: a missing or unfit live answer
is recorded with the reason in `refused`. Each track a routing step takes is added to that question's row when it is taken; more
than one step can route on one decision (a `Decide` followed by two `Switch` steps on the same
choice), so the row keeps every routing in order rather than only the last.

Recording is required, not best effort. A decision that cannot be written fails its step before
any track is taken, [classified](/docs/core/trains-and-junctions#classifying-failures) `Transient`
(or `Permanent` when the store refuses the value), because a re-queue of that run would otherwise
have nothing to replay. A shadow's answer can never cost the live record: a number JSON cannot
hold is written as the string `"NaN"`, `"Infinity"` or `"-Infinity"`, and an answer of a type the
journal has no stored form for is recorded with the reason in the shadow's error. A live answer of
such a type fails its step, `Permanent`, since it could never be read back. So does a decision the
run's own train reports under an external id other than the run's (the train changed its
`ExternalId` while running), rather than being acted on without a record. Decisions are matched
to the run through its async flow; when code in the run lost that flow (it suppressed
`ExecutionContext` flow, say), the journal looks the run up by its external id among the runs of
that train in progress on this host, and records against it when exactly one matches. Otherwise
the decision is logged only. Calling
`AddDecisionRecording()` more than once registers it once.

Each answer is stored with the hash of the state it was given about
([`StateHash`](/docs/core/decisions#a-replay-matches-the-state)), never the
state itself. `AddDecisionRecording(o => o.ReplayAnswersFor(...))` sets how long a recorded answer
may be replayed; see [Only into the same state, and only while fresh](#only-into-the-same-state-and-only-while-fresh).

### Keying the state hash

The hash covers every value in the state, including members marked `[TraxSensitive]`, and is stored
beside the answer, so give the host a key and the hash is an HMAC that only a holder of the key can
compute. `AddDecisionRecording` takes one of three, in order:

| Source | How |
|---|---|
| A [`StateHashKey`](/docs/core/decisions#keying-the-state-hash) the host registered in the container itself | Kept as it is |
| `HashStatesWith(byte[] key)` | `AddDecisionRecording(o => o.HashStatesWith(key))`, at least 32 bytes |
| Configuration | Base64 of at least 32 bytes under `Trax:Decisions:StateHashKey` (`DecisionRecordingOptions.StateHashKeyConfigurationKey`) |

```csharp
effects.UsePostgres(connectionString)
    .AddDecisionRecording(o => o.HashStatesWith(Convert.FromBase64String(secret)))
```

Every process that may repeat a run must use the same key. Adding or changing it means each answer
recorded before it is asked afresh once, because a hash taken under another key, or none, never
matches. A configured value that is not base64 or is shorter than 32 bytes is not ignored: the state
is not hashed at all, so nothing replays, rather than falling back to an unkeyed hash.

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
key, answer, track and decider of every later decision, refusal and route of the run. Which questions are
sensitive is decided by the type the question is about (`DecisionMade.QuestionType`), with
inheritance, so a subclass of a marked type is withheld even when no scan saw it.

| Column | Holds |
|---|---|
| `metadata_id` | The run. Rows are deleted with it. |
| `question_key` | The [question key](/docs/core/decisions#question-keys): the type's name without its namespace, or the `Key` set on `[Asks]` |
| `occurrence` | Which asking of the question this was in the run, from 0 |
| `fingerprint` | The [fingerprint](/docs/core/decisions#observing-and-replaying) of the asking the answer was given to, 64 lowercase hex characters. A replay hands it back, and an answer whose fingerprint differs from the question as it is asked now is not replayed. |
| `kind` | `choice`, `score` or `yes_no` |
| `question` | The question's instructions and criteria (jsonb) |
| `answer` | The answer acted on (jsonb), with `replay_refused` when an earlier run's answer was not replayed and the decider was asked afresh. On a refused row, the answer the run would not act on, or null when the decider gave none. |
| `refused` | Why the run would not act on the decider's answer, or null for an answer it acted on. Every row has an `answer` or a `refused` (a check constraint holds it). A refused row is never replayed. |
| `model` | The model that answered, as the decider names it, or null for a decider that is not a model. For a System One model it is the name the request asked for, echoed back. |
| `decider` | The decider's type, or null for a replayed answer |
| `replayed` | Whether the answer came from an earlier run |
| `shadows` | Each shadow's answer, whether it agreed, and why it gave none (jsonb) |
| `state_hash` | The hash of the state the question was asked about: `k1:` and the hex of an HMAC-SHA256 under the host's key, or `s1:` and the hex of a SHA-256 without one. Null when the state could not be hashed, when no key is configured and the state can hold a `[TraxSensitive]` member, or when the row predates the column. A row with no hash is never replayed. |
| `routes` | Every track a routing step took on this decision, in order, as a jsonb array of `{"track": ..., "fallback_reason": ...}`; `fallback_reason` says why the decision was not followed, and is null when it was. Null when nothing routed on it. |
| `decided_at` | When it was answered |

The same migration adds `decisions_recorded` to `trax.metadata`. It is set on a run's first write
when the host records decisions, before any junction, so a replay can tell a run that reached no
questions from one whose decisions were never recorded. `replay_abandoned` (Postgres `061`, Sqlite
`026`) marks a manifest's retry that named a run to replay and could not honour it; see
[A requeue of a requeue](#a-requeue-of-a-requeue).

It needs a data provider, and is a compile error before one. The table ships in the core
migration set (Postgres `054`, Sqlite `019`; `state_hash` in Postgres `059`, Sqlite `024`) and is
read through `IDataContext.RecordedDecisions`.

### Other decision observers

Trax.Core takes one `IDecisionObserver` from the container. When Trax adds an observer of its own
(`AddDecisionRecording`, `AddJunctionEvents`), it registers a composite in its place that tells
every observer: each one Trax adds, and each one the host registered as an `IDecisionObserver`
before `AddTrax`. Required observers are told
first, so a decision that could not be recorded is never reported as made, then the best-effort
ones.

Register your own observer before `AddTrax`. One registered after it replaces the composite in the
container, so decision recording would never be told about a decision, and junction events would
not hear the routings they withhold tracks by. While decision recording or junction events are
registered, such a host refuses to start with an `InvalidOperationException` naming the observer,
and every run refuses too, for a host built without the generic host. No decision is acted on
unrecorded. Decorating `IDecisionObserver` (Scrutor's `Decorate`, say) replaces the composite the
same way and is refused the same way.

Each observer in the composite is built on its own. A host observer that is best effort as
registered (an instance that is not `Required`, or a type that leaves `Required` to its default)
and cannot be built is left out with a warning. Any other observer that cannot be built, including
one registered through a factory, fails every decision step and the host's start, with what
building it threw.

```csharp
services.AddSingleton<IDecisionObserver, DecisionAuditor>();   // before AddTrax: told alongside Trax's own
services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording()));
```

```sql
-- How often each support track was taken this week, and how often it was overruled.
-- question_key is the type's name without its namespace, unless [Asks] sets a Key.
SELECT r ->> 'track' AS track, count(*) AS routings, count(r ->> 'fallback_reason') AS overruled
FROM trax.decision d
JOIN trax.metadata m ON m.id = d.metadata_id
CROSS JOIN LATERAL jsonb_array_elements(d.routes) AS r
WHERE d.question_key = 'TicketTrack' AND m.start_time > now() - interval '7 days'
GROUP BY 1;
```

That is the data a confidence bar should be tuned from: label a few hundred recorded decisions
with what should have happened, and pick the bar where the cost of a wrong track meets the cost
of sending work to the fallback.

## Re-queued and retried runs replay their decisions

Re-queueing an execution, with the dashboard's **Re-queue** button or the
[`requeueExecution`](/docs/sdk-reference/graphql-api/mutations#requeueexecution) mutation, queues a
run that replays the original's recorded decisions. Both go through
[`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync),
which sets the new run's `ReplayDecisionsOf` to the original's id when the original has decisions
to replay: it recorded a decision it acted on, or was itself queued to replay another run. A run of a train
that never decides is re-queued as an ordinary enqueue. A run's answers are replayed once: when a
queued entry or another run already replays the run being re-queued, the requeue asks afresh and
its message says so. **Re-queue, Ask Afresh** and `requeueExecution(askAfresh: true)` ask afresh on
purpose. Each question the new run asks is answered
from what was recorded for the same question and asking, without calling a decider or its shadows,
and recorded with `replayed` set. A re-queue repeats a run, usually because something after a
decision failed, and asking a model again could take a different track. `Trax.Docs/adr/0041`
records why.

A manifest's automatic retry, and a requeue of its dead letter, replay the failed run's decisions
too, at most once in a row and only when the scheduler can show the answers were given by that
manifest's own run, about the same input. See
[Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions).
Queueing a train through `queueTrain` or `QueueTrainAsync` never replays, and neither does a
manifest's scheduled run that is not a retry.

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

```csharp
effects.UsePostgres(connectionString)
    .AddDecisionRecording(o => o.ReplayAnswersFor(TimeSpan.FromHours(6)))
```

`ReplayAnswersFor` takes at least one second; `TimeSpan.MaxValue`, or any span longer than the
calendar goes back, means no bound. An answer outside either bound is asked afresh, and
that is never a failure. A changed state is reported in the new row's `replay_refused`; an aged-out
answer is left out of the replay and logged at `Information`. Rows written before `state_hash`
existed have none, so the first requeue after upgrading asks afresh. Both bounds apply to every
path that names a run to replay: a manual requeue, a dead letter's requeue and a manifest's retry.
`Trax.Effect/docs/adr/0020` records why.

### A requeue of a requeue

A replay follows `replay_decisions_of` back through every run it repeats. For each question and
occurrence the nearest run's recorded answer wins, since that is what the nearest run acted on,
whether it replayed it or was answered afresh. A question that run never reached falls back to the
run it replayed, and so on. So a requeue of a requeue that failed before reaching a question still
takes the track the first run took there. The chain is followed at most 32 runs back.

A manifest's retry that named a run to replay and asked afresh because the replay could not be
honoured (below) is marked `replay_abandoned`, on its first write. It never acted on the answers of the run it named, so a replay of it stops
there: it replays that run's own recorded answers, and fails, as for any run that did not record
its decisions, when it recorded none. A run that names a run to replay without the mark (one that
never started, say) is passed through to the run it names, as before.

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

A run replays the decisions of another run only when it was queued to: by a manifest's automatic
retry, a requeue of the manifest's dead letter, or a requeue of an execution
(`requeueExecution`, the dashboard's **Re-queue**). A train run directly (`Run`, a `[TraxQuery]` or
`[TraxMutation]` in Run mode) or queued with `queueTrain` / `QueueTrainAsync` never replays, and
neither does a manifest's scheduled run that is not a retry. So a train whose crash should be
recovered without asking the model again has to run from a manifest, and every one of these must
hold:

| Piece | Why | What fails without it |
|---|---|---|
| `AddDecisionRecording()` after the data provider, on every host that runs the train | It writes the answers a retry replays, and is the `IDecisionReplay` | Before a data provider: a compile error. On a host that runs a deciding train without it: the host refuses to start, naming the trains |
| An `IDecider` the container or Memory supplies | Something must answer the first time | The startup check refuses the chain |
| Postgres or Sqlite, not InMemory | Retry replay compares the work queue entry's input, which the InMemory scheduler does not keep | Nothing fails; the retry asks afresh |
| The run comes from a manifest with `MaxRetries` of 1 or more | Only a manifest's retry replays on its own | No retry, or a retry with nothing to replay |
| The manifest's input is byte-identical between attempts | The scheduler links a retry only to a failed run queued with exactly the same input | The retry asks afresh |
| `ReplayDecisionsOnRetry` left on (the default) | It is the manifest's switch | The retry asks afresh |
| The state at each decision is the same on the retry | Each answer replays only into a state with the same hash: no timestamp, random id, counter or cache in it | That answer is asked afresh, with `replay_refused` |
| A state hash key, when a state can reach a `[TraxSensitive]` member | Without one, such a decision is stored with no hash | That answer never replays, and a warning names the state type once |
| The crash happens after the decisions | Trax has no per-junction retry: the retry runs the chain from its first junction, and only the answers are replayed | Nothing to save |
| Any `IDecisionObserver` of your own registered before `AddTrax` | One registered after it replaces the composite recording depends on | The host refuses to start, naming the observer |

Trax.Api has no GraphQL operation that creates a manifest. A host whose callers start such runs over
GraphQL exposes its own mutation, a `[TraxMutation]` train whose junction calls
[`ITraxScheduler.ScheduleOnceAsync`](/docs/sdk-reference/scheduler-api/manifest-management#scheduleonceasync)
with `options => options.MaxRetries(n)`, and returns the manifest's `Id`, which
`operations.executions(manifestId:)` takes to list each attempt. Keep anything that varies between
attempts, a fault-injection flag for instance, out of the manifest's input. The
[Recovery sample](/docs/samples/recovery) does exactly this.

What a retry does in each case:

| Between the failure and the retry | The retry |
|---|---|
| Nothing changed | Is linked to the failed run (`replay_decisions_of`); every answer replays, `replayed` set, no decider asked |
| Data the state is built from changed | Is still linked; the replay refuses each answer whose state hashes differently, stores the reason in `replay_refused` and asks afresh. Not `replay_abandoned` |
| The manifest's input was edited | Is not linked: asks afresh |
| `triggerManifest(askAfresh: true)` before the dispatcher claimed the retry | Is not linked: asks afresh |
| The failed run was deleted, or the retry ran on a host without `AddDecisionRecording()` | Was linked, could not honour it: asks afresh and is marked `replay_abandoned` |
| The failed run had itself replayed another run | Is not linked: a retry replays at most once in a row |

To see why an answer was asked afresh, read `trax.decision` through `IDataContext.RecordedDecisions`
(entity `RecordedDecision`, namespace `Trax.Effect.Models.RecordedDecision`): `Replayed`, and in
the `Answer` JSON a `replay_refused` property with the reason. The run's
`Metadata.ReplayDecisionsOf` and `Metadata.ReplayAbandoned` say whether it was linked and whether the
link was abandoned. Neither GraphQL nor junction events carry these: a step says `replayed`, not why
it was not. [`IOperationsService.GetRecordedDecisionsAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#recorded-decisions)
reads a run's rows a page at a time, with an answer to a `[TraxSensitive]` question, and every
decision on a track taken on one, left out.

```csharp
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

await using var context = await dataContextFactory.CreateDbContextAsync(ct);   // IDataContextProviderFactory
var rows = await context.RecordedDecisions.AsNoTracking()
    .Where(d => d.MetadataId == metadataId).OrderBy(d => d.Id).ToListAsync(ct);
var refused = rows.Select(d => d.Answer is null
    ? null
    : JsonNode.Parse(d.Answer)?["replay_refused"]?.GetValue<string>());
```

### A host that does not record

A requeue is linked only when the run has decisions to replay, so a replay reaches a host without
`AddDecisionRecording()` when one host recorded the run and another runs the requeue: one worker of
a fleet left without the call. Every host that runs trains (`AddScheduler`, `AddTraxJobRunner`,
and `AddTraxWorker` through it) checks for this at startup. When `IDecisionReplay` is not
registered and some registered train's chain, or a track in it, asks a decider, the host refuses
to start: an `InvalidOperationException` lists those trains and says to call
`AddDecisionRecording()`. The check runs as the host starts, before any worker claims work.

Trax fails closed here rather than warning. The replay on such a host would already fail rather
than ask afresh, but only when a requeue happened to land on it, and a warning on one worker of a
fleet is easy to miss. Refusing to start puts the gap in front of whoever deploys the host, before
it takes any work.

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) | [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) | [TrainExecution](/docs/sdk-reference/mediator-api/train-execution)
