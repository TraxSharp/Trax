---
title: Runner authorization posture
description: "The postures a Trax runner accepts work under (signing key, policy, unsigned), what each transport checks, how refusals look and where nonces are kept."
type: reference
area: scheduling
nav_order: 22
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/TraxJobRunnerOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/RequestSigning
  - Trax.Scheduler/src/Trax.Scheduler/Extensions/JobRunnerExtensions.cs
---

# Runner authorization posture

A runner runs the work it is sent as trusted infrastructure and skips per-train `[TraxAuthorize]`
checks, because the scheduler authorized the work when it was submitted. Every runner entry point
(`UseTraxJobRunner`, `UseTraxRunEndpoint`, `SqsJobRunnerHandler`, `TraxLambdaFunction`) refuses
to start until `AddTraxJobRunner(runner => ...)` sets a posture. The options are listed on
[AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner). Why the runner is
trusted is in [Choosing where trains run](/docs/scheduler/remote-execution#authorization-posture).

## Postures

| Posture | Applies to | What the runner does |
|---------|-----------|----------------------|
| `runner.SigningKey = key` | every entry point | Verifies a `Trax-Signature` over the exact request body before reading it |
| `runner.AuthorizationPolicy = "name"` | `UseTraxJobRunner`, `UseTraxRunEndpoint` | Applies the named ASP.NET Core authorization policy. The policy must admit only the scheduler: one that admits end users lets them run any registered train, gated or not |
| `runner.AllowUnsignedRequests()` | every entry point | Accepts every request, and logs a warning naming the entry point at startup. `TraxLambdaFunction.RunLocalAsync`'s local routes honour it only for callers on the loopback address |

A signing key and a policy can be combined; the runner then requires both. With neither and no
`AllowUnsignedRequests()`, mapping an endpoint throws
[`InvalidOperationException`](/docs/scheduler/startup-errors#runner).

## Signed requests

The key is 32 or more random bytes, given to both sides: `SigningKey` on `UseRemoteWorkers`,
`UseRemoteRun`, `UseLambdaWorkers`, `UseLambdaRun` or `UseSqsWorkers`, and on `AddTraxJobRunner`
(or `ConfigureRunner` in a `TraxLambdaFunction`).

Scheduler:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.UseRemoteWorkers(
                remote =>
                {
                    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
                    remote.SigningKey = runnerKey;
                },
                routing => routing.ForTrain<IReportTrain>()
            )
        )
);
```

Runner:

```csharp compile
builder.Services.AddTraxJobRunner(runner => runner.SigningKey = runnerKey);

app.UseTraxJobRunner("/trax/execute");
app.UseTraxRunEndpoint("/trax/run");
```

The signature is an HMAC-SHA256 over the request's purpose (`execute` or `run`), a timestamp, a
random nonce and the body. It travels in the `Trax-Signature` HTTP header, in the
`LambdaEnvelope`'s `Signature`, or in an SQS message attribute of the same name. A request signed
for one purpose does not verify for the other. The scheduler signs each retry afresh, so a retry is
never refused as a replay.

### What each transport checks

| Transport | Checked |
|-----------|---------|
| HTTP (`/trax/execute`, `/trax/run`, and `RunLocalAsync`) | Signature, timestamp within `MaxClockSkew` (default 5 minutes), nonce not seen before |
| Lambda `Run` (synchronous) | Signature, timestamp, nonce |
| Lambda `Execute` (asynchronous) and SQS | Signature only: both redeliver the same message, and the job's `Pending` metadata row stops a second run |

### Refusals

| Request | Result |
|---------|--------|
| HTTP request with a missing or invalid signature | `401`, before any of the body is read; the train is not reached |
| HTTP body over `MaxRequestBodyBytes` (default 8 MiB) | `413` |
| Lambda invocation or SQS message that fails verification | Throws, so Lambda's retry and dead-letter settings apply |

Refusals are logged as a warning at most once a minute, with a count, and at `Debug` otherwise.

### Where nonces are kept

A signing runner records each accepted nonce in the `runner_nonce` table, which the standard
migrations create, so every instance of a runner that shares the database accepts a request once
between them. The table's primary key decides: a nonce already recorded and not yet expired is a
repeat. Any other database failure while recording one is an error, never read as a repeat.
Expired rows are taken over or removed as it goes.

| Store | When |
|-------|------|
| Database (`runner_nonce`) | Default; needs a relational data provider (`UsePostgres` or `UseSqlite`) |
| In memory, `runner.UseInMemoryNonceStore()` | A runner that runs as a single instance |
| A registered `INonceStore` singleton | Nonces shared some other way |

A signing runner on a host with no relational data provider and neither of the other two refuses
to start.

## Policy-based authorization

The policy is named in the runner options, not chained onto the endpoint with
`.RequireAuthorization()`, so the runner can check it at startup. The scheduler sends the
credentials the policy expects through `ConfigureHttpClient`.

Scheduler:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.UseRemoteWorkers(
                remote =>
                {
                    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
                    remote.ConfigureHttpClient = client =>
                        client.DefaultRequestHeaders.Add(
                            "Authorization",
                            $"Bearer {schedulerToken}"
                        );
                },
                routing => routing.ForTrain<IReportTrain>()
            )
        )
);
```

Runner:

```csharp compile
builder.Services.AddAuthorization(o =>
    o.AddPolicy("trax-scheduler", p => p.RequireClaim("scope", "trax:dispatch"))
);
builder.Services.AddTraxJobRunner(runner => runner.AuthorizationPolicy = "trax-scheduler");

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxJobRunner("/trax/execute");
```

## What a runner will run

A queued job names its input type, and the runner accepts only a name that is the input type of
one of its registered trains. The job's metadata row must belong to the train registered for that
input; otherwise the runner refuses it and the row stays `Pending`. The scheduler's own trains (the
ManifestManager, the JobDispatcher, the JobRunner and the two cleanup trains) do not count as
registered: `/trax/run` and a Lambda `Run` refuse their names, and a queued job whose input belongs
to one is refused with its row left `Pending`. A host train that shares one of their short names
still runs by that name.

## See also

- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner)
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers)
- [Choosing where trains run](/docs/scheduler/remote-execution)

## SDK Reference

> [AddTraxJobRunner / UseTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers)
