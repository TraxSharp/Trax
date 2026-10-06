---
layout: default
title: Energy Hub
description: "The EnergyHub sample: a hub that schedules and serves GraphQL but runs no jobs, standalone workers that do, and lifecycle events carried home over RabbitMQ."
parent: Samples & Deployment
nav_order: 9
---

# Energy Hub

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/DistributedWorkers` in [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) splits
scheduling from execution. One process, the hub, owns the GraphQL API, the scheduler and the
dashboard. Separate worker processes run every job the hub queues. The two share PostgreSQL (the
`background_job` table) and RabbitMQ (lifecycle events), and nothing else, so you can run as many
workers as the load needs.

## What it proves

| Feature | Where |
|---|---|
| A scheduler that queues but never executes: `OverrideSubmitter` registers `PostgresJobSubmitter` alone, so no local worker starts | `Hub/Program.cs`, `HubExecutesNoTrainsTests` |
| A standalone worker: `AddTraxWorker` claims jobs from `background_job` with `FOR UPDATE SKIP LOCKED` | `Worker/Program.cs` |
| A completion the worker publishes reaches a GraphQL subscription on the hub, over RabbitMQ | `CrossProcessEventTests` |
| Gated mutations and a gated `operations` namespace on a hub that keeps one anonymous query | `GraphQLTests`, `OperationsCredentialTests` |
| A worker that runs `[TraxAuthorize]` trains without an API of its own (`AllowMissingAuthorizationService`) | `Worker/Program.cs` |
| Interval, cron, dependent and batch manifests declared on the hub | `ManifestConfigurationTests` |

## Layout

```
samples/DistributedWorkers/
├── Trax.Samples.EnergyHub/          trains, manifest names, roles
├── Trax.Samples.EnergyHub.Hub/      GraphQL API + scheduler + dashboard (port 5202)
└── Trax.Samples.EnergyHub.Worker/   AddTraxWorker (port 5203, serves nothing)
```

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d       # Postgres on 5432 (database trax_energyhub), RabbitMQ on 5672 (user trax, password trax123)

# Terminal 1: the hub, in Development, on http://localhost:5202
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub

# Terminal 2: a worker; start more to scale out
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
```

`dotnet run` starts both in Development through `Properties/launchSettings.json`. That is the only
environment that registers the demo operator key and serves the dashboard at
<http://localhost:5202/trax>. Started any other way, the hub serves no dashboard and accepts no key,
so its mutations and `operations` namespace refuse every caller until you register real credentials.

## Try it

The anonymous query runs on the hub itself:

```bash
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId totalKwh efficiency } } } }"}'
```

```json
{"data":{"discover":{"solar":{"monitorSolarProduction":{"arrayId":"SPA-001","totalKwh":142.7,"efficiency":0.89}}}}}
```

Queue a grid trade with the operator key. The mutation returns at once; a few seconds later the
worker's console logs the trade and the hub's does not:

```bash
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
  -d '{"query":"mutation { dispatch { tradeGridEnergy(input: {ratePerKwh: 0.14, maxSellPercent: 80}) { externalId workQueueId } } }"}'
```

Read the scheduler's manifests, then try the same query without the key:

```bash
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
  -d '{"query":"{ operations { manifests(take: 5) { items { externalId scheduleType } } } }"}'

curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ operations { manifests(take: 5) { items { externalId } } } }"}'
```

The second answers `{"errors":[{"message":"Not authorized.","path":["operations"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],"data":{"operations":null}}`.

These commands are also in the hub's `Program.cs` header, and `DocumentedExamplesTests` reads them
from there and runs them, so the header cannot drift from the schema.

## How it works

### The hub schedules and queues, and runs nothing it queues

```csharp
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .UseBroadcaster(b =>
                    b.UseRabbitMq(rabbitMqConnectionString, o => o.ExchangeName = LifecycleEvents.ExchangeName)
                )
        )
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .OverrideSubmitter(services =>
                    services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
                )
                .Schedule<IMonitorSolarProductionTrain>(
                    ManifestNames.MonitorSolarProduction,
                    new MonitorSolarProductionInput { ArrayId = "SPA-001", Region = "somerset" },
                    Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(1))
                )
                // ... more manifests
        )
);
```

A scheduler on Postgres registers `PostgresJobSubmitter` **and** starts `LocalWorkerService` by
default, so a hub without `OverrideSubmitter` runs its own jobs and races the workers for them.
Naming the submitter registers it alone: dispatched jobs are written to `background_job` and wait
there for a worker. See [Remote Execution: Standalone Workers](/docs/scheduler/remote-execution#model-3-standalone-workers-poll-based).

What still runs on the hub is anything answered synchronously: a `[TraxQuery]`, and a mutation in
`RUN` mode. That is why every EnergyHub mutation is declared `GraphQLOperation.Queue`, and why its one
query, a live sensor read, is the only train the hub executes.

### The worker

```csharp
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.EnergyHub;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .UseBroadcaster(b =>
                    b.UseRabbitMq(rabbitMqConnectionString, o => o.ExchangeName = LifecycleEvents.ExchangeName)
                )
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                .AllowMissingAuthorizationService()
        )
);

builder.Services.AddTraxWorker(opts =>
{
    opts.WorkerCount = 4;
    opts.PollingInterval = TimeSpan.FromSeconds(1);
});

var app = builder.Build();
app.Run();
```

The worker references the same train assembly, so it can resolve every train the hub queues. The
mutations carry `[TraxAuthorize(Roles = "Operator")]`, and the mediator refuses to start a host that
has such trains and no `ITrainAuthorizationService`. The worker has no API and never sees a caller:
the hub checked the role when the job was queued, and work the scheduler hands over is trusted.
`AllowMissingAuthorizationService()` says exactly that. See
[Authorization: Opting Out for Scheduler-Only Hosts](/docs/authorization#opting-out-for-scheduler-only-hosts).

### Events come home over RabbitMQ

Both processes call `UseBroadcaster(b => b.UseRabbitMq(...))` with the same broker and the same
exchange, `energyhub.lifecycle` (`LifecycleEvents.ExchangeName`). Every process bound to an exchange
receives every event on it, so a sample of its own needs an exchange of its own, or another Trax
application on the broker would receive its events. The worker publishes each run's lifecycle events
to that fanout exchange; the hub's
`TrainEventReceiverService` consumes them, and because the hub calls `AddTraxGraphQL()`, Trax forwards
them to its GraphQL subscriptions. A subscriber on the hub therefore sees `onTrainCompleted` for a
trade that ran on a worker:

```graphql
subscription { onTrainCompleted { externalId trainName output } }
```

The connection string must name a user the broker accepts. A refused connection does not fail
anything: the receiver logs a warning and retries forever, and a failed publish never fails a run, so a
wrong password silently turns cross-process subscriptions off. The sample, its tests,
`docker-compose.yml` and CI all use `amqp://trax:trax123@localhost:5672`. See
[UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster).

### Who may operate the hub

```csharp
using Trax.Api.Auth.ApiKey;

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", EnergyHubRoles.Operator)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .MaxExecutionDepth(6)
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: EnergyHubRoles.Operator)
);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
```

`GateOperations` gates the `operations` namespace alone, so the anonymous solar query keeps working,
while each mutation is gated by its own `[TraxAuthorize]`. The dashboard has no login in front of it,
so it is registered and served only in Development; `UseTraxDashboard()` refuses to start until the
dashboard has a posture, which `AllowAnonymousDashboard()` gives it there. See
[API Security](/docs/api-security) and [Dashboard](/docs/dashboard).

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.EnergyHub.E2E
```

The suite starts the hub and a worker in one test process with two `WebApplicationFactory`
instances, against the `energyhub_e2e_tests` database. `TRAX_TEST_PG_PORT` moves Postgres and
`TRAX_TEST_RABBITMQ` replaces the broker URI.

| Test class | Proves |
|---|---|
| `HubExecutesNoTrainsTests` | The hub has no `LocalWorkerService` and its submitter is `PostgresJobSubmitter`; the worker runs the local worker |
| `CrossProcessEventTests` | A trade the worker runs reaches an `onTrainCompleted` subscriber on the hub |
| `GraphQLTests` | The query is anonymous, a queued report completes on the worker, an anonymous trade is refused |
| `OperationsCredentialTests` | The control plane refuses an anonymous read or trigger and admits the operator |
| `DocumentedExamplesTests` | Every curl command in the hub's header works as written |
| `ManifestConfigurationTests`, `DependencyChainTests` | The manifests the hub declares, and the dependent battery train following the solar read |

## SDK Reference

> [AddScheduler / OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions)
