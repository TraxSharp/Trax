---
layout: default
title: Decision Recording and Models
description: How Trax.Effect records each decision against its run, replays them when a run is re-queued, and answers questions with Nimble or another typed model.
parent: Effect
nav_order: 8
---

# Decision Recording and Models

A train's [decisions](/docs/core/decisions) run without Trax.Effect; Trax.Core needs only an
`IDecider`. Trax.Effect adds three things around them: an adapter for typed decision models, a
record of every decision against the run that made it, and re-queued runs that take the tracks
the original took.

```csharp
services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording()
    .AddNimbleDecider(o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))));
```

## Nimble

[Nimble](https://docs.bespokelabs.ai/nimble/overview) is Bespoke Labs' typed decision model, and
the one Trax builds around: its 9B weights are open (published under Apache 2.0, a fine-tune of
Qwen3.5-9B), so a train's decisions can run on hardware you own, with no data leaving it and no
per-request price.

`AddNimbleDecider` talks to a Nimble server you run, started from Nimble's own serving code
(`nimble/serving/server.py` in `bespokelabsai/nimble`), which answers `POST /v1/systemone`. There
is no default endpoint. Nimble's documentation describes no production hosted API, and the public
demo it links to is unauthenticated and runs on one GPU, so it is not somewhere to send a train's
state and Trax does not point at it. A host that registers Nimble without an `Endpoint` fails to
start.

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
`200` whose body is not a System One response (including one with no `answers` object) is retried
with a doubling, jittered wait that stops growing at `MaxRetryDelay` (30 seconds by default). A
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
| `routes` | Every track a routing step took on this decision, in order, as a jsonb array of `{"track": ..., "fallback_reason": ...}`; `fallback_reason` says why the decision was not followed, and is null when it was. Null when nothing routed on it. |
| `decided_at` | When it was answered |

The same migration adds `decisions_recorded` to `trax.metadata`. It is set on a run's first write
when the host records decisions, before any junction, so a replay can tell a run that reached no
questions from one whose decisions were never recorded.

It needs a data provider, and is a compile error before one. The table ships in the core
migration set (Postgres `054`, Sqlite `019`) and is read through `IDataContext.RecordedDecisions`.

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

## Re-queued runs replay their decisions

Re-queueing an execution, with the dashboard's **Re-queue** button or the
[`requeueExecution`](/docs/sdk-reference/graphql-api/mutations#requeueexecution) mutation, queues a
run that replays the original's recorded decisions. Both go through
[`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync),
which sets the new run's `ReplayDecisionsOf` to the original's id when the original has decisions
to replay: it recorded a decision it acted on, or was itself queued to replay another run. A run of a train
that never decides is re-queued as an ordinary enqueue. Each question the new run asks is answered
from what was recorded for the same question and asking, without calling a decider or its shadows,
and recorded with `replayed` set. A re-queue repeats a run, usually because something after a
decision failed, and asking a model again could take a different track. `Trax.Docs/adr/0041`
records why.

The requeue is the only way to set the link. Queueing a train through `queueTrain` or
`QueueTrainAsync` never replays, and neither does a dead-letter retry or a manifest's scheduled run.

### A requeue of a requeue

A replay follows `replay_decisions_of` back through every run it repeats. For each question and
occurrence the nearest run's recorded answer wins, since that is what the nearest run acted on,
whether it replayed it or was answered afresh. A question that run never reached falls back to the
run it replayed, and so on. So a requeue of a requeue that failed before reaching a question still
takes the track the first run took there. The chain is followed at most 32 runs back.

All of this is loaded once, before the run's first junction, so answering a question never waits
on the database. Metadata cleanup does not delete a run while a `Queued` work queue entry or any
other run names it in `replay_decisions_of`, so a chain stays whole while anything links to it;
when the linking run expires too, it is deleted first and the run it links to in a later batch or
sweep.

| The replay meets | What happens |
|---|---|
| A question no run in the chain reached (they failed earlier, or the chain changed since) | Asked afresh |
| A recorded answer whose [fingerprint](/docs/core/decisions#observing-and-replaying) differs from the question as it is asked now, or that no longer fits it (an option renamed or removed, a scale with fewer levels, another kind of question) | Asked afresh, with the reason stored as `replay_refused` |
| A recorded choice of a member the switch has no track for | Replayed; it takes the `Otherwise` track again, as it did the first time |
| A refused row (the step failed on the decider's answer) | Skipped: never replayed, so the question is asked afresh |
| A host that does not call `AddDecisionRecording()` | The run fails before its first junction, `Permanent` |
| A run in the chain that no longer exists, or is a run of another train | The run fails before its first junction, `Permanent` |
| A run in the chain that ran without recording its decisions (`decisions_recorded` false, and it replayed nothing itself) | The run fails before its first junction, `Permanent`: what it decided cannot be known |
| A chain that leads back on itself, or goes back more than 32 runs | The run fails before its first junction, `Permanent` |
| A recorded answer that cannot be read | The run fails before its first junction, `Permanent` |
| A database failure while loading the chain | The run fails before its first junction, `Transient` |

A run that cannot honour its replay fails instead of asking afresh, because it was queued to
repeat the original. A run in the chain that recorded its decisions but reached no questions is
not a failure: there is nothing of its own to repeat, and the replay goes on to the run before it.

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

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) | [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) | [TrainExecution](/docs/sdk-reference/mediator-api/train-execution)
