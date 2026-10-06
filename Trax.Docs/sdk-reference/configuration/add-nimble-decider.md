---
layout: default
title: AddNimbleDecider
description: Reference for AddNimbleDecider, which answers train decisions with Nimble, Bespoke Labs' open-weights decision model, on a Nimble server you run.
parent: Configuration
grand_parent: SDK Reference
nav_order: 22
---

# AddNimbleDecider

Answers every train's [decisions](/docs/core/decisions) with Nimble, Bespoke Labs' open-weights
typed decision model, on a Nimble server you run from Nimble's own serving code. Registers a
`SystemOneDecider` as the `IDecider`, or, with a name, as a keyed `SystemOneDecider` only. See
[Nimble](/docs/effect/decisions#nimble).

## Signature

```csharp
public static TBuilder AddNimbleDecider<TBuilder>(
    this TBuilder configurationBuilder,
    Action<NimbleOptions> configure
)
    where TBuilder : TraxEffectBuilder

public static TBuilder AddNimbleDecider<TBuilder>(
    this TBuilder configurationBuilder,
    string name,
    Action<NimbleOptions> configure
)
    where TBuilder : TraxEffectBuilder
```

## NimbleOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Endpoint` | `Uri?` | required | The full URL of `POST /v1/systemone` on your Nimble server, such as `http://localhost:8000/v1/systemone`. There is no default: Nimble has no production hosted API to fall back on. Must be `http` or `https`, and HTTPS unless it is a loopback address. |
| `ApiKey` | `string?` | `null` | Sent as a bearer token. The server checks one only when it is started with `OPENJEV_API_KEY`; leave it unset, or blank, for one that is not. Never logged. |
| `Model` | `string` | `bespokelabs/Bespoke-Nimble-9B` | The checkpoint id the server answers to. Set it when your server is deployed under another name. `nimble-latest` is refused. |
| `MaxConcurrentRequests` | `int?` | `4` | Requests in flight at once, or null for no limit. One server container runs four evaluations at once and answers a fifth with 529; raise this when the server scales to more containers. |
| `MaxOptions` | `int` | `26` | The most options or levels one question may offer, the server's limit. Raise it only for a server whose prompt code accepts more. |
| `AttemptTimeout` | `TimeSpan` | 30 seconds | How long one attempt may take. Longer than for a hosted model, because a self-hosted server can be slow on its first request after loading. |
| `MaxAttempts` | `int` | `3` | Attempts per request, counting the first. |
| `RetryDelay` | `TimeSpan` | 500 ms | The wait before the first retry, doubling after each, with jitter. |
| `MaxRetryDelay` | `TimeSpan` | 30 seconds | The longest wait before a retry. A `Retry-After` asking for longer ends the retries. |

A request carries at most 64 questions (`NimbleOptions.MaxQuestionsPerRequest`). The constants
`DefaultModel`, `DefaultMaxConcurrentRequests` and `DefaultMaxOptions` hold the defaults above.

The request cannot pin a model revision: the server serves the revision it was deployed with and
echoes back the name it was asked for. Pin the revision where the server is deployed, and re-check
your confidence bars when you change it.

## Returns

`TBuilder`, the same builder type that was passed in.

## Examples

```csharp
// A Nimble server on this machine.
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDecisionRecording()
        .AddNimbleDecider(o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))
    )
);

// One GPU machine serving Nimble to several hosts, behind an API key.
effects.AddNimbleDecider(o =>
{
    o.Endpoint = new Uri("https://nimble.internal.example/v1/systemone");
    o.ApiKey = configuration["Nimble:ApiKey"];
});

// Named, as the first tier of a cascade you compose yourself.
effects.AddNimbleDecider("nimble", o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"));
```

## Remarks

- The options are checked when this is called, so a missing endpoint, an endpoint that is not
  `http` or `https`, a floating model name or plain HTTP to another machine stops the host from
  starting.
- The unnamed form may be called once, and counts against
  [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider)'s unnamed form:
  a second unnamed System One decider fails at startup. The named form registers a keyed
  `SystemOneDecider` under `name` and nothing else; a name used twice fails at startup.
- Retries, failure classes and what an unreadable answer does are those of
  [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider#failures). A
  request over the server's token or body limits is answered 413 or 422 and fails as `Permanent`.
- To put Nimble in front of a larger model, register it by name and compose a `CascadingDecider`
  as the `IDecider`; see
  [Putting the model in front of a larger one](/docs/effect/decisions#putting-the-model-in-front-of-a-larger-one).

## Package

```
dotnet add package Trax.Effect.Decisions.SystemOne
```
