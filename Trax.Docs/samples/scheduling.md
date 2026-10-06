---
layout: default
title: Scheduling Sample
description: "A complete scheduler host: interval, cron, one-off, dependent and dormant manifests, retries with backoff, a dead letter and its requeue over GraphQL."
parent: Samples & Deployment
nav_order: 1
---

# Scheduling Sample

`samples/Scheduling` in [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) is one ASP.NET
process that schedules trains, runs them on the built-in local workers, retries the ones that
fail and dead-letters the ones that keep failing. The Trax GraphQL operations surface and the
dashboard show and steer it. It shows the scheduling surface and nothing else, and its E2E suite
proves every behaviour on this page against the running host.

## What it proves

| Manifest | Declared with | What it shows |
|---|---|---|
| `refresh-exchange-rates` | `Schedule(..., Every.Seconds(5))` | An interval manifest |
| `reprice-catalog` | `ThenInclude(...)` | A dependent: runs after each successful refresh |
| `alert-rate-spike` | `Include(..., o => o.Dormant())` | A dormant dependent: runs only when the refresh activates it, with the input the refresh gives it |
| `send-daily-digest` | `Schedule(..., Cron.Daily(hour: 7))` | A cron manifest, evaluated in UTC |
| `send-launch-announcement` | `ScheduleOnce(..., TimeSpan.FromSeconds(10))` | A one-off that disables itself after its first success |
| `import-supplier-feed` | `Schedule(..., Every.Seconds(15), o => o.MaxRetries(2))` | A failing train: two retries with backoff, a dead letter, and a requeue that runs it again |

The supplier the last train calls is down for its first three calls after startup. So the first
run fails, the retry 2 seconds later fails, the retry 4 seconds after that fails, and the manifest
is dead-lettered. Requeue the dead letter and the run succeeds, because the outage is over.

## Run it

You need the .NET 10 SDK and Docker. From the `Trax.Samples` folder:

```bash
docker compose up -d database
dotnet run --project samples/Scheduling/Trax.Samples.Scheduling.Host
```

The host listens on `http://localhost:5230` and starts in Development (its
`launchSettings.json` says so). It reads the `TraxDatabase` connection string from
`appsettings.json`, `Host=localhost;Port=5432;Database=trax_scheduling;Username=trax;Password=trax123`,
a database `docker compose` creates (see [Running the samples](/docs/samples#running-the-samples) for a volume made before it). Trax creates its `trax` schema on first start.

| URL | What |
|---|---|
| `http://localhost:5230/trax` | The dashboard. Development only, no sign-in |
| `http://localhost:5230/trax/graphql` | The GraphQL endpoint. `operations` needs the header `X-Api-Key: operator-key-do-not-use-in-production` |

## Try it

Within about ten seconds of starting, the dashboard's **Data > Dead Letters** page lists
`import-supplier-feed`, and **Data > Metadata** shows its three failed runs, each with the
junction that threw (`DownloadFeedJunction`), the exception type and the stack trace.

The same over GraphQL:

```bash
KEY='X-Api-Key: operator-key-do-not-use-in-production'
URL=http://localhost:5230/trax/graphql

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "{ operations { deadLetters { deadLetters(status: AWAITING_INTERVENTION) { items { id manifestName reason } } } } }"}'
```

```json
{"data":{"operations":{"deadLetters":{"deadLetters":{"items":[{"id":1,
  "manifestName":"Trax.Samples.Scheduling.Trains.ImportSupplierFeed.IImportSupplierFeedTrain",
  "reason":"Max retries exceeded: (3) failures > (2) max retries"}]}}}}}
```

Requeue it, then read it back. Use the `id` the query returned; on a fresh database it is `1`:

```bash
curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "mutation { operations { deadLetters { requeueDeadLetter(id: 1) { success workQueueId message } } } }"}'
# {"data":{"operations":{"deadLetters":{"requeueDeadLetter":{"success":true,"workQueueId":14,"message":"Dead letter requeued"}}}}}

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "{ operations { deadLetters { deadLetter(id: 1) { status retryMetadataId } } } }"}'
# {"data":{"operations":{"deadLetters":{"deadLetter":{"status":"RETRIED","retryMetadataId":81}}}}}
```

`retryMetadataId` is the run the requeue started; it completed. Run the daily digest now, outside
its schedule:

```bash
curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "mutation { operations { triggerManifest(externalId: \"send-daily-digest\") { success message } } }"}'
# {"data":{"operations":{"triggerManifest":{"success":true,"message":"Manifest triggered"}}}}
```

Leave the key off and every `operations` field answers
`{"errors":[{"message":"Not authorized.","extensions":{"code":"TRAX_AUTHORIZATION"}}]}`.

## How it is built

Two projects, following the [trains library pattern](/docs/samples#the-trains-library-pattern):

```
samples/Scheduling/
  Trax.Samples.Scheduling/            class library: trains, ManifestNames, the two fake services
  Trax.Samples.Scheduling.Host/       Microsoft.NET.Sdk.Web: Program.cs, appsettings.json, DemoKeys
```

The library references `Trax.Effect`, `Trax.Mediator` and `Trax.Scheduler` (the last for
`IDormantDependentContext`). The host adds `Trax.Effect.Data.Postgres`,
`Trax.Effect.Provider.Parameter`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL` and `Trax.Dashboard`.

### Program.cs

The whole file, less its banner comment:

```csharp
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduling;
using Trax.Samples.Scheduling.Host;
using Trax.Samples.Scheduling.Services;
using Trax.Samples.Scheduling.Trains.AlertRateSpike;
using Trax.Samples.Scheduling.Trains.ImportSupplierFeed;
using Trax.Samples.Scheduling.Trains.RefreshExchangeRates;
using Trax.Samples.Scheduling.Trains.RepriceCatalog;
using Trax.Samples.Scheduling.Trains.SendDailyDigest;
using Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddSingleton<ExchangeRateFeed>();
builder.Services.AddSingleton<SupplierFeed>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).SaveTrainParameters())
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // Demo speed. The production defaults are in the comments.
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1)) // default 5 s
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1)) // default 2 s
                .DefaultRetryDelay(TimeSpan.FromSeconds(2)) // default 5 min
                .RetryBackoffMultiplier(2.0) // default 2.0
                .MaxRetryDelay(TimeSpan.FromSeconds(30)) // default 1 h
                .ConfigureLocalWorkers(workers =>
                    workers.PollingInterval = TimeSpan.FromMilliseconds(250) // default 1 s
                )
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = TimeSpan.FromMinutes(30);
                    cleanup.AddTrainType<IRefreshExchangeRatesTrain>(TimeSpan.FromDays(1));
                    cleanup.AddTrainType<IRepriceCatalogTrain>(TimeSpan.FromDays(1));
                })
                .Schedule<IRefreshExchangeRatesTrain>(
                    ManifestNames.RefreshExchangeRates,
                    new RefreshExchangeRatesInput { BaseCurrency = "USD" },
                    Every.Seconds(5)
                )
                .ThenInclude<IRepriceCatalogTrain>(
                    ManifestNames.RepriceCatalog,
                    new RepriceCatalogInput { Catalog = "storefront" }
                )
                .Include<IAlertRateSpikeTrain>(
                    ManifestNames.AlertRateSpike,
                    new AlertRateSpikeInput(),
                    options => options.Dormant()
                )
                .Schedule<ISendDailyDigestTrain>(
                    ManifestNames.SendDailyDigest,
                    new SendDailyDigestInput { Audience = "subscribers" },
                    Cron.Daily(hour: 7)
                )
                .ScheduleOnce<ISendLaunchAnnouncementTrain>(
                    ManifestNames.SendLaunchAnnouncement,
                    new SendLaunchAnnouncementInput(),
                    TimeSpan.FromSeconds(10)
                )
                .Schedule<IImportSupplierFeedTrain>(
                    ManifestNames.ImportSupplierFeed,
                    new ImportSupplierFeedInput { Supplier = "acme" },
                    Every.Seconds(15),
                    options => options.MaxRetries(2)
                )
        )
);

// NO WARRANTY: a demo key, registered only in Development.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: DemoKeys.OperatorRole)
);

if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(options => options.AllowAnonymousDashboard());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();

app.UseTraxGraphQL();

app.Run();

namespace Trax.Samples.Scheduling.Host
{
    public partial class Program;
}
```

`DemoKeys` holds two constants: `OperatorRole = "Operator"` and
`OperatorKey = "operator-key-do-not-use-in-production"`.

### Registration order

| Call | Must come | Why |
|---|---|---|
| `AddTrax(...)` with `AddEffects`, then `AddMediator`, then `AddScheduler` | First | The builder types enforce the order inside it at compile time: `AddScheduler` exists only on what `AddMediator` returns |
| `AddTraxApiKeyAuth`, `AddAuthentication`, `AddAuthorization` | Anywhere | The subscription interceptor and `GateOperations` read the registered schemes once the container is built |
| `AddTraxGraphQL(...)` | After `AddTrax` | It throws `AddTrax() must be called before AddTraxGraphQL()` otherwise |
| `AddTraxDashboard(...)` | After `AddTrax` | It throws `AddTraxDashboard() requires AddTrax() to be called first` otherwise, and lists only the trains registered before it |
| `UseAuthentication`, `UseAuthorization` | Before `UseTraxDashboard` and `UseTraxGraphQL` | So the dashboard and the operations gate see the caller |

### The schedules

**Interval.** `Every.Seconds(5)` counts from the manifest's last success, so consecutive runs start
at least five seconds apart, plus up to a polling interval or two. An interval manifest that has
never run is due on the first poll after the host starts.

**Dependent.** `ThenInclude` makes `reprice-catalog` depend on the manifest declared just before it.
It has no schedule: the scheduler queues it once its parent has succeeded since its own latest run
started. Disable the parent and the dependent stops too.

**Dormant dependent.** `Include` parents from the root `Schedule` rather than from the previous
call, so `alert-rate-spike` also depends on `refresh-exchange-rates`. `.Dormant()` means the
parent's success never queues it. `FlagSpikeJunction`, in the parent's chain, activates it with
the reading as its input:

```csharp
public class FlagSpikeJunction(IDormantDependentContext dormants, ILogger<FlagSpikeJunction> logger)
    : Junction<(RefreshExchangeRatesInput Input, RateReading Reading), RefreshExchangeRatesOutput>
{
    public override async Task<RefreshExchangeRatesOutput> Run(
        (RefreshExchangeRatesInput Input, RateReading Reading) input
    )
    {
        var (settings, reading) = input;
        var spike = Math.Abs(reading.ChangePercent) > settings.SpikeThresholdPercent;

        if (spike)
            await dormants.ActivateAsync<IAlertRateSpikeTrain, AlertRateSpikeInput, Unit>(
                ManifestNames.AlertRateSpike,
                new AlertRateSpikeInput
                {
                    BaseCurrency = reading.BaseCurrency,
                    ChangePercent = reading.ChangePercent,
                }
            );

        return new RefreshExchangeRatesOutput(reading.BaseCurrency, reading.ChangePercent, spike);
    }
}
```

The context knows which manifest's run it is in, so it can only activate dormant dependents
declared under that manifest. Run the same train on the train bus, outside the scheduler, and
`ActivateAsync` logs a warning and queues nothing.

**Cron.** `Cron.Daily(hour: 7)` is stored as `0 7 * * *` and evaluated in UTC. A new cron manifest
does not run at startup: seeding records its next occurrence in `NextScheduledRun`. Triggering it
by hand counts as a run; after that success the next run is the next occurrence after it.

**One-off.** `ScheduleOnce` stores `ScheduledAt` as the seed time plus the delay. Once that passes,
the manifest runs; after its first success it sets `IsEnabled = false` and never runs on its own
again, even if re-enabled. Every start re-seeds it, which moves a one-off that has not run yet to
the new start plus the delay, so a host that restarts more often than the delay never reaches it.

**Retries and the dead letter.** `MaxRetries(2)` allows the first run and two retries. The retry
delay is `DefaultRetryDelay * RetryBackoffMultiplier ^ (failures - 1)`, capped at `MaxRetryDelay`:

| Attempt | Waits after the previous failure | Result |
|---|---|---|
| 1 | | Fails: `HttpRequestException`, `Supplier 'acme' answered 503 Service Unavailable.` |
| 2 | at least 2 s | Fails |
| 3 | at least 4 s | Fails; the next ManifestManager cycle writes a dead letter with reason `Max retries exceeded: (3) failures > (2) max retries` |
| | | No fourth attempt: a manifest with a dead letter awaiting intervention is skipped |

A retry is not a separate timer. It is the manifest's next due run, held back by the backoff. An
interval or cron manifest that fails when it was due stays due, so it is retried as soon as the
backoff passes. A run started off-schedule, by a trigger, that fails is retried only once the
manifest is next due by its schedule.

Requeueing the dead letter (the dashboard, the `requeueDeadLetter` mutation or
`ITraxScheduler.RequeueDeadLetterAsync`) queues a run with the manifest's own input, marks the dead
letter `Retried` and, once the run is dispatched, sets the dead letter's `RetryMetadataId` to it.
Resolving a dead letter either way resets the failure count, so the requeued run starts a fresh
budget of retries. Acknowledging resolves it without running anything, and a later requeue of the
same dead letter is refused (`success: false`).

### Demo speed

| Setting | Sample | Default | Effect here |
|---|---|---|---|
| `ManifestManagerPollingInterval` | 1 s | 5 s | How soon a due manifest, a retry or a dead letter is noticed |
| `JobDispatcherPollingInterval` | 1 s | 2 s | How soon a queued entry is handed to a worker |
| `DefaultRetryDelay` | 2 s | 5 min | The first retry's wait |
| `RetryBackoffMultiplier` | 2.0 | 2.0 | Doubles each further wait |
| `MaxRetryDelay` | 30 s | 1 h | Caps the wait |
| Local worker `PollingInterval` | 250 ms | 1 s | How soon a worker claims a dispatched job |

Keep the defaults in production. One second is the floor for both polling intervals.

### The operations surface and who may use it

`ExposeOperationQueries()` and `ExposeOperationMutations()` add the `operations` namespace;
`GateOperations(roles: "Operator")` requires that role on all of it. The demo key carries the role
and is registered only in Development. Started in Production the host still starts, registers no
key at all, and refuses every `operations` call with `TRAX_AUTHORIZATION`; the dashboard is not
mapped, so `/trax` is a 404. Register real keys with `AddHashed` or a resolver
([AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)) before serving it.

## What fails at startup

Each of these refuses to start the host with an `InvalidOperationException`; the messages are the
ones the sample printed when the line was changed.

| Change to the sample | Message starts |
|---|---|
| Drop `.GateOperations(...)` | `ExposeOperationMutations() exposes scheduler-control mutations (...) but the GraphQL endpoint is not gated and the namespace carries no gate of its own` |
| `.GateOperations()` with no policy or roles | `GateOperations() needs a policy or roles: GateOperations(policy: "...") or GateOperations(roles: "...")` |
| `AddTraxDashboard()` with no posture | `UseTraxDashboard() needs to know who may use the dashboard` |
| Register the demo key outside Development | `AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production', which marks a published demo key, and the environment is 'Production'` |
| `cleanup.DeleteBatchSize = 20_000` | `The scheduler configuration has values it cannot run with. AddMetadataCleanup: DeleteBatchSize must be between 1 and 10000.` |

`AddScheduler` checks every interval, delay and count against its range when the host builds; the
ranges are in [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges).

## Testing it

`tests/Trax.Samples.Scheduling.E2E` starts the real host with `WebApplicationFactory` against its
own database, `scheduling_e2e_tests` (port overridable with `TRAX_TEST_PG_PORT`), and asserts on
the Trax tables and the GraphQL endpoint:

- Drop the `trax` schema before the host starts, so every run sees a first start: the one-off
  still due, the supplier down. The host recreates the schema.
- Keep the scheduler running. Before acting, read the newest metadata id, then wait for runs of
  one manifest newer than it. Waits poll the database until a condition holds or a budget runs
  out, never a fixed sleep.
- Mark the assembly `[NonParallelizable]`: the tests share one host and steer the same manifests.
- Run a metadata cleanup sweep on demand by resolving `IMetadataCleanupTrain` from the host's
  services and calling `Run(new MetadataCleanupRequest())`.
- Start a second factory with `UseEnvironment("Production")` to prove the demo key and the
  dashboard are gone.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ThenInclude / Include](/docs/sdk-reference/scheduler-api/dependent-scheduling) | [IDormantDependentContext](/docs/sdk-reference/scheduler-api/dependent-scheduling#idormantdependentcontext) | [ScheduleOnce / TriggerAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard)

Feature pages: [Scheduling](/docs/scheduler), [Dependent Trains](/docs/scheduler/dependent-trains),
[Delayed / One-Off Jobs](/docs/scheduler/delayed-jobs),
[Dead Letters & Cleanup](/docs/scheduler/dead-letters-and-cleanup),
[Operations queries](/docs/sdk-reference/graphql-api/queries#operations-queries).
