---
title: Answer decisions with Nimble
description: "Point a train's decisions at a Nimble server you run, put it in front of a larger model, and tune its confidence bars from recorded decisions."
type: how-to
area: effects
nav_order: 9
source:
  - Trax.Effect/src/Trax.Effect.Decisions.SystemOne
---

# Answer decisions with Nimble

This page assumes a train that already asks [decisions](/docs/core/decisions). What the adapter
sends and refuses is on [Decision records](/docs/effect/decisions#typed-decision-models).

## 1. Run a Nimble server

Start a server from Nimble's own serving code (`nimble/serving/server.py` in
`bespokelabsai/nimble`). It answers `POST /v1/systemone`. It runs SGLang and needs an NVIDIA GPU;
on a Mac with Apple Silicon, serve the model through Nimble's MLX scorer instead, as the Recovery
sample does ([Nimble on a Mac](/docs/samples/recovery#nimble-on-a-mac)).

Pin the model revision where you deploy the server. The request cannot pin one, so the server
answers with whatever it was deployed with.

## 2. Register the decider

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

`Endpoint` is required: there is no default, and the host does not start without one. A server
on another machine must be HTTPS. When the server is started with `OPENJEV_API_KEY`, set `ApiKey`:

```csharp compile
effects.AddNimbleDecider(o =>
{
    o.Endpoint = new Uri("https://nimble.internal.example/v1/systemone");
    o.ApiKey = configuration["Nimble:ApiKey"];
});
```

`AddDecisionRecording` is optional for answering, and required for step 4 and for
[replaying a run's answers](/docs/effect/recover-a-deciding-train). The other settings
(`MaxConcurrentRequests`, `MaxOptions`, timeouts and retries) are on
[AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider).

Send a state that holds only what the questions need. These models answer less accurately with
detail that does not bear on the question.

## 3. Put Nimble in front of a larger model (optional)

Register each model by name, then compose them into the `IDecider` every train asks. A question
Nimble is unsure of goes to the second model:

```csharp compile
using Trax.Core.Decisions;
using Trax.Effect.Decisions.SystemOne;

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

Which answers escalate is on
[Escalating what the fast decider is unsure of](/docs/core/decisions#escalating-what-the-fast-decider-is-unsure-of).
To use another System One model on its own, call `AddSystemOneDecider` without a name in place of
`AddNimbleDecider`: moving between them is a change of endpoint and model name.

## 4. Tune the confidence bars

Check each switch's confidence bars against your own labelled cases, not a published benchmark.
The recorded decisions are the data: this query counts how often each track was taken this week,
and how often the decision was overruled.

```sql
-- question_key is the type's name without its namespace, unless [Asks] sets a Key.
SELECT r ->> 'track' AS track, count(*) AS routings, count(r ->> 'fallback_reason') AS overruled
FROM trax.decision d
JOIN trax.metadata m ON m.id = d.metadata_id
CROSS JOIN LATERAL jsonb_array_elements(d.routes) AS r
WHERE d.question_key = 'TicketTrack' AND m.start_time > now() - interval '7 days'
GROUP BY 1;
```

Label a few hundred recorded decisions with what should have happened, and pick the bar where the
cost of a wrong track meets the cost of sending work to the fallback. Re-check the bars whenever
the server's model revision changes.

## SDK Reference

> [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording)
