---
layout: default
title: AddSystemOneDecider
description: Reference for AddSystemOneDecider, which answers train decisions through a typed decision model that speaks the System One format, such as Jev.
parent: Configuration
grand_parent: SDK Reference
nav_order: 23
---

# AddSystemOneDecider

Answers every train's [decisions](/docs/core/decisions) through a typed decision model that speaks
the System One request format (Jev, or another server that accepts it), by registering a
`SystemOneDecider` as the `IDecider`, or, with a name, as a keyed `SystemOneDecider` only. For
Nimble, use [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider), which fills
in its model name and limits. See
[Typed decision models](/docs/effect/decisions#other-typed-decision-models).

## Signature

```csharp
public static TBuilder AddSystemOneDecider<TBuilder>(
    this TBuilder configurationBuilder,
    Action<SystemOneOptions> configure
)
    where TBuilder : TraxEffectBuilder

public static TBuilder AddSystemOneDecider<TBuilder>(
    this TBuilder configurationBuilder,
    string name,
    Action<SystemOneOptions> configure
)
    where TBuilder : TraxEffectBuilder
```

## SystemOneOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Endpoint` | `Uri?` | required | The model's endpoint, such as `https://api.typesafe.ai/v1/systemone` for Jev, or the URL of a server you run. Requests go to it and nowhere else: redirects are not followed. Must be an `http` or `https` URL, and HTTPS unless it is a loopback address. |
| `Model` | `string?` | required | The model pinned to a version, such as `jev-1.13.0`. A floating alias (ending `latest`, or with no version digits) is refused. |
| `AllowFloatingModel` | `bool` | `false` | Accepts a floating alias. |
| `ApiKey` | `string?` | `null` | Sent as a bearer token. A blank or whitespace key counts as none and sends no `Authorization` header. Never logged. |
| `AttemptTimeout` | `TimeSpan` | 10 seconds | How long one attempt may take before it is abandoned and retried. |
| `MaxAttempts` | `int` | `3` | Attempts per request, counting the first. |
| `RetryDelay` | `TimeSpan` | 500 ms | The wait before the first retry, doubling after each, with jitter, unless the model sends `Retry-After`. The doubling stops at `MaxRetryDelay`. |
| `MaxRetryDelay` | `TimeSpan` | 30 seconds | The longest wait before a retry, at most a day. A `Retry-After` asking for longer is not retried: the decision fails as `Transient`. |
| `MaxOptions` | `int` | `255` | The most options or levels one question may offer, from 2 to 255. A question with more is refused at startup. |
| `MaxQuestions` | `int` | `64` | The most questions one request may carry. A `Decide` asking more is refused at startup. |
| `MaxConcurrentRequests` | `int?` | `null` | Requests in flight at once, or no limit. A request over the limit waits for a slot, which does not count against its `AttemptTimeout`. |

## Returns

`TBuilder`, the same builder type that was passed in.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDecisionRecording()
        .AddSystemOneDecider(o =>
        {
            o.Endpoint = new Uri("https://api.typesafe.ai/v1/systemone");
            o.Model = "jev-1.13.0";
            o.ApiKey = configuration["Jev:ApiKey"];
        })
    )
);
```

## Failures

| What the model did | Retried | Run fails with | Failure class |
|---|---|---|---|
| 408, 429 or 5xx other than 501 and 505 (including Nimble's 529, busy), no answer in time, a connection refused, reset or timed out, or a 200 whose body is not a System One response, including one with no `answers` object or one in a character set that cannot be decoded | yes, until `MaxAttempts` | `DecisionServiceException` | `Transient` |
| A `Retry-After` longer than `MaxRetryDelay` | no | `DecisionServiceException` | `Transient` |
| 501, 505 or any other 4xx (bad criteria, a bad key, 402 for no credit left) | no | `DecisionServiceException` | `Permanent` |
| A 3xx. Redirects are not followed, including one handed back by an `HttpClient` you pass in; the endpoint is the one configured. | no | `DecisionServiceException` | `Permanent` |
| An endpoint that cannot be reached as configured: its name does not resolve, its TLS handshake fails, or a proxy refuses the credentials | no | `DecisionServiceException` | `Permanent` |
| A request that cannot be sent as it is: a state written as JSON `null`, or one that cannot be serialized | no | `DecisionServiceException` | `Permanent` |
| An answer it cannot read | no | the train's unanswered-question failure | `Transient` |

`SystemOneDecider` implements [`IVetsQuestions`](/docs/core/decisions#deciders), so what the model
refuses whatever the state stops the host at startup instead of failing every run: more questions
in one `Decide` than `MaxQuestions`, a question with blank instructions, fewer than two or more than
`MaxOptions` options or levels, an option named twice, a kind of question the format cannot ask,
and a state type JSON writes as a bare number, `true` or `false`. Each question is sent under its
[question key](/docs/core/decisions#question-keys), the type's name without its namespace or the
`Key` set on `[Asks]`.

A choice or score answer without a `confidence`, which the format allows, takes the probability of
the chosen option or the nearest level instead; one with neither is left out. A score whose
`probabilities` are not keyed by every level from 0 is left out rather than shifted onto the wrong
levels.

A retry waits for what `Retry-After` asks, plus a little jitter so a fleet of callers does not
return at once, or else the doubling `RetryDelay`, jittered and capped at `MaxRetryDelay`.
Cancelling the run cancels the request and is not retried. A failure's message gives the status,
a few words on what it means, and the provider's request id from the `x-typesafe-request-id`
response header when there is one; the request id is what the provider asks for when a failure is
reported. The response body is never read into the message: it can echo the request, which carries
the train's state, and the message is stored as the run's failure reason.

Every answer carries the `model` the response names. That is the name the request asked for,
echoed back, not a version the server confirms, so pin the version where the model is deployed.

## Remarks

- The options are checked when this is called, so unusable settings stop the host from starting.
- The unnamed form registers the decider as `SystemOneDecider` and as `IDecider`, and may be used
  once across this method and `AddNimbleDecider`; a second unnamed registration fails at startup
  instead of replacing the first.
- The named form registers a keyed `SystemOneDecider` under `name` and nothing else, so several
  models can sit side by side. Resolve each with `GetRequiredKeyedService<SystemOneDecider>(name)`
  and compose them, for example into a `CascadingDecider` registered as the `IDecider`; see
  [Putting the model in front of a larger one](/docs/effect/decisions#putting-the-model-in-front-of-a-larger-one).
  `DecidedBy<TDecider>()` resolves by type, so it cannot name a keyed decider.
- The decider is built on first use and owned by the container, which disposes its HTTP client
  when the host stops. A decision in flight when the decider is disposed keeps the outcome of its
  request.
- A `SystemOneDecider` built with its own HTTP client does not follow redirects. One built with an
  `HttpClient` you pass in uses that client, and a 3xx it hands back still fails the decision.

## Package

```
dotnet add package Trax.Effect.Decisions.SystemOne
```
