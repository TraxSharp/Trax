---
layout: default
title: Content Shield
description: "The ContentShield sample: an API that executes nothing, an AWS Lambda style runner signed with a shared key, and how to test the runner without AWS."
parent: Samples & Deployment
nav_order: 10
---

# Content Shield

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/EphemeralWorkers` in [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) is a content
moderation API whose trains all run somewhere else: on a runner that is an AWS Lambda function in
production and a local Kestrel server in development. The API holds no workers and writes no
`background_job` rows. It POSTs each queued job to the runner and returns, and it POSTs each
synchronous run and waits for the answer.

## What it proves

| Feature | Where |
|---|---|
| Queued mutations dispatched over HTTP with `UseRemoteWorkers`; nothing reaches `background_job` | `RemoteExecutionTests` |
| Every synchronous run (run mutations and `[TraxQuery]` queries) sent to the runner with `UseRemoteRun` | `RemoteExecutionTests.Query_is_run_on_the_runner` |
| A runner that is a `TraxLambdaFunction`, served locally by `RunLocalAsync` | `Runner/Function.cs`, `Runner/Program.cs` |
| A signing key shared by both sides; an unsigned request to the runner gets `401` | `RunnerSigningKey.cs`, `AuthorizationTests` |
| The runner's lifecycle events reaching a GraphQL subscriber on the API over RabbitMQ, on the sample's own `contentshield.lifecycle` exchange | `CrossProcessEventTests` |
| Testing a Lambda runner end to end without AWS | `tests/Trax.Samples.ContentShield.E2E/Fixtures/TestRunner.cs` |

## Layout

```
samples/EphemeralWorkers/
├── Trax.Samples.ContentShield/          trains, roles, RunnerSigningKey
├── Trax.Samples.ContentShield.Api/      GraphQL + dispatch + dashboard (port 5204)
└── Trax.Samples.ContentShield.Runner/   Function : TraxLambdaFunction; Program.cs runs it locally (port 5205)
```

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d       # Postgres on 5432 (database trax_contentshield), RabbitMQ on 5672 (user trax, password trax123)

# Terminal 1: the runner on http://localhost:5205
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner

# Terminal 2: the API on http://localhost:5204
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api
```

Both start in Development through `Properties/launchSettings.json` (`ASPNETCORE_ENVIRONMENT` for the
API, `DOTNET_ENVIRONMENT` for the runner) and sign with a published demo key. Anywhere else, set
`Trax__RunnerSigningKey` to the same value on both, the base64 of 32 or more random bytes
(`openssl rand -base64 32`); without it the API refuses to start with
`Set Trax:RunnerSigningKey to the base64 of 32 or more random bytes ...`.

## Try it

```bash
# Look up a moderation result (anonymous; the API waits while the runner runs it)
curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ discover { moderation { lookupModerationResult(input: {contentId: \"test-001\"}) { contentId moderationStatus classification threatScore } } } }"}'

# Queue a review (anonymous). It returns at once; the runner's console logs the review.
curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"mutation { dispatch { moderation { reviewContent(input: {contentId: \"test-002\", contentType: \"video\", contentBody: \"suspicious video content\"}) { externalId workQueueId } } } }"}'

# Run a report on the runner and wait for the output (Moderator key, Development only)
curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: contentshield-moderator-key-do-not-use-in-production" \
  -d '{"query":"mutation { dispatch { reports { generateModerationReport(input: {reportPeriod: \"Daily\"}) { externalId output { totalReviewed totalFlagged topViolationTypes falsePositiveRate } } } } }"}'

# The runner refuses anything not signed with the key
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5205/trax/execute \
  -H "Content-Type: application/json" -d '{}'
```

The report answers with its output, and the last command prints `401`. The same report without the
key answers `Not authorized.` with code `TRAX_AUTHORIZATION`.

## How it works

### The API dispatches everything

```csharp
using Trax.Samples.ContentShield;
using Trax.Scheduler.Extensions;

var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"] ?? "http://localhost:5205";
var runnerKey = RunnerSigningKey.Resolve(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .UseBroadcaster(b =>
                    b.UseRabbitMq(rabbitMqConnectionString, o => o.ExchangeName = LifecycleEvents.ExchangeName)
                )
        )
        .AddMediator(mediator => mediator.ScanAssemblies(typeof(ReviewContentTrain).Assembly))
        .AddScheduler(scheduler =>
            scheduler
                .UseRemoteWorkers(
                    remote =>
                    {
                        remote.BaseUrl = $"{runnerBaseUrl}/trax/execute";
                        remote.SigningKey = runnerKey;
                    },
                    routing =>
                        routing
                            .ForTrain<IReviewContentTrain>()
                            .ForTrain<ISendViolationNoticeTrain>()
                            .ForTrain<IGenerateModerationReportTrain>()
                )
                .UseRemoteRun(remote =>
                {
                    remote.BaseUrl = $"{runnerBaseUrl}/trax/run";
                    remote.SigningKey = runnerKey;
                })
        )
);
```

`UseRemoteWorkers` routes the named trains' queued runs to the runner. A train it does not route
still falls back to the local worker, which is why the API process still registers
`LocalWorkerService`; every queueable ContentShield train is routed, so that worker never has a job.

`UseRemoteRun` replaces the in-process run executor for **every** synchronous run, which includes
`[TraxQuery]` queries as well as mutations in `RUN` mode. So even `lookupModerationResult` travels to
the runner. Leave `UseRemoteRun` out if queries should run on the API.

### The runner is a Lambda function

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Runner.Lambda;
using Trax.Samples.ContentShield;
using Trax.Scheduler.Configuration;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UsePostgres(configuration.GetConnectionString("TraxDatabase")!)
                        .UseBroadcaster(b =>
                            b.UseRabbitMq(
                                configuration.GetConnectionString("RabbitMQ")!,
                                o => o.ExchangeName = LifecycleEvents.ExchangeName
                            )
                        )
                )
                .AddMediator(typeof(ReviewContentTrain).Assembly)
        );
    }

    // Without a posture the function refuses every request.
    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = RunnerSigningKey.Resolve(configuration, IsDevelopment(configuration));
}
```

A runner runs what it is sent as already authorized, so it refuses every request until it knows who
may send work. The signing key is that answer: the API signs each request body with it, and the runner
checks the `Trax-Signature` header before reading the body. See
[Remote Execution: Authorization Posture](/docs/scheduler/remote-execution#authorization-posture).

The function reads its configuration from `appsettings.json` next to the binary and from environment
variables (`ConnectionStrings__TraxDatabase`, `Trax__RunnerSigningKey`, `DOTNET_ENVIRONMENT`). It does
not see command-line arguments, and it has no `IHostEnvironment`, which is why `IsDevelopment` reads
`DOTNET_ENVIRONMENT` from configuration.

Locally, `Program.cs` serves the function over HTTP:

```csharp
await new Function().RunLocalAsync([$"--contentRoot={AppContext.BaseDirectory}", .. args]);
```

The Kestrel server `RunLocalAsync` starts reads `appsettings.json` from its content root, and so
does the function, from the copy next to the binary. The `--contentRoot` argument names that
directory explicitly. From the next Trax.Runner.Lambda release `RunLocalAsync` defaults to it, but
the released versions default to the current directory: started from the repository root without the
argument, that runner found no Kestrel endpoint and listened on 5000 instead of 5205, and the API's
requests went nowhere.

For production, swap `UseRemoteWorkers` and `UseRemoteRun` for `UseLambdaWorkers` and `UseLambdaRun`
(package `Trax.Scheduler.Lambda`), which invoke the function through the AWS SDK with no public
endpoint, with the same `SigningKey`. See
[Lambda Workers](/docs/scheduler/remote-execution#model-2c-lambda-workers-direct-invocation).

### Who may do what

Anyone may submit content (`reviewContent`) and look up a result. `sendViolationNotice` and
`generateModerationReport` carry `[TraxAuthorize(Roles = "Moderator")]`; the only key with that role
is a demo key registered in Development. The runner needs no authorization service for them: a remote
run executes inside a trusted scope, because the API already checked the caller. The dashboard is
registered and served only in Development, with `AllowAnonymousDashboard()`.

Only the Moderator trains, `sendViolationNotice` and `generateModerationReport`, are
`[TraxBroadcast]`. A broadcast train's events carry every run's output to every subscriber the train
admits, so the anonymous review and lookup are not broadcast, and a subscriber on the API sees only
the Moderator trains.

The API and the runner both publish on `contentshield.lifecycle` (`LifecycleEvents.ExchangeName`),
an exchange of their own, so another Trax application on the same broker does not receive their
events. See [UseBroadcaster: RabbitMQ](/docs/sdk-reference/configuration/use-broadcaster#rabbitmq).

Subscriptions need a credential even for anonymous trains. Once an API-key scheme is registered,
every WebSocket must carry a key in its `connection_init` payload, and a socket without one is
refused; see [Subscriptions: Authentication](/docs/sdk-reference/graphql-api/subscriptions#authentication).

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.ContentShield.E2E
```

No AWS is involved. The suite starts the real `Function` through `RunLocalAsync` on a free port, from
a subclass that overrides `BuildServiceProvider` only to supply the test database and broker and to
count the requests the runner handles, and points the API's `Runner:BaseUrl` at it. Both use the
`contentshield_e2e_tests` database.

| Test class | Proves |
|---|---|
| `RemoteExecutionTests` | A query and a run mutation reach `/trax/run`, a queued review reaches `/trax/execute`, and no `background_job` row is written |
| `AuthorizationTests` | Reports and notices refuse an anonymous caller; the runner refuses unsigned requests on both routes |
| `CrossProcessEventTests` | A violation notice the runner sends reaches an `onTrainCompleted` subscriber on the API; an anonymous review's result is not broadcast |
| `DocumentedExamplesTests` | Every curl command in the API's header works as written |

## SDK Reference

> [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
