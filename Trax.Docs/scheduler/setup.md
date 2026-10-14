---
title: Schedule your first train
description: "A tutorial: a host that runs a train every 30 seconds and another nightly from Postgres, reads what the scheduler recorded and runs a job on demand."
type: tutorial
area: scheduling
nav_order: 1
snippet_context: none
---

# Schedule your first train

We build a small host that runs a report train every 30 seconds and another every night, with
both schedules stored in Postgres. Then we read what the scheduler recorded, run the nightly job on
demand, and quiet the scheduler's own logs. Each step ends with something you can see.

Every C# block on this page is compiled in CI. You need the .NET 10 SDK and Docker.

## 1. Create the project and start Postgres

```bash
dotnet new web -n Reports
cd Reports
```

Replace `Reports.csproj` with this:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Effect" Version="1.61.0" />
    <PackageReference Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageReference Include="Trax.Mediator" Version="1.61.0" />
    <PackageReference Include="Trax.Scheduler" Version="1.61.0" />
  </ItemGroup>
</Project>
```

Start a database and point the application at it:

```bash
docker run -d --name reports-db -p 5432:5432 \
  -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
```

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

`docker ps` lists `reports-db` as running.

## 2. Define the train

The scheduler stores each job's input in the database, so the input implements
`IManifestProperties`. A train whose input does not implement it cannot be passed to the
scheduling methods.

`Report.cs`:

```csharp compile=interval,nightly,trigger
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Reports;

public record ReportInput : IManifestProperties
{
    public string Region { get; init; } = "";
}

public class BuildReportJunction(ILogger<BuildReportJunction> logger)
    : EffectJunction<ReportInput, Unit>
{
    public override Task<Unit> Run(ReportInput input)
    {
        logger.LogInformation("Building the {Region} report", input.Region);
        return Task.FromResult(Unit.Default);
    }
}

public interface IReportTrain : IServiceTrain<ReportInput, Unit>;

public class ReportTrain : ServiceTrain<ReportInput, Unit>, IReportTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<BuildReportJunction>().Resolve();
}
```

`dotnet build` succeeds. Nothing runs the train yet.

## 3. Run it every 30 seconds

`Program.cs`:

```csharp compile=interval
using Reports;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IReportTrain>(
                "report-eu",
                new ReportInput { Region = "eu" },
                Every.Seconds(30)
            )
        )
);

var app = builder.Build();
app.Run();
```

```bash
dotnet run
```

Within a few seconds the log shows `Building the eu report`, and again every 30 seconds after
that. `UsePostgres` created the `trax` schema on start, `Schedule` stored the job as a manifest
named `report-eu`, and the scheduler ran it on worker threads in this process.

## 4. Read what the scheduler recorded

In a second terminal:

```bash
docker exec reports-db psql -U trax -d trax -c \
  "select external_id, schedule_type, interval_seconds, last_successful_run from trax.manifest"

docker exec reports-db psql -U trax -d trax -c \
  "select name, train_state, start_time from trax.metadata
   where manifest_id is not null order by id desc limit 5"
```

The first query shows one manifest, `report-eu`, with a 30-second interval and the time of its
last successful run. The second shows one row per run of it, each named `Reports.IReportTrain`
and `Completed`. A run that fails is recorded too, with the junction that failed and its
exception, and the scheduler retries it.

## 5. Add a nightly job with retries

Add a second manifest for the same train, on a cron schedule, allowed five retries.

`Program.cs`:

```csharp compile=nightly
using Reports;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .Schedule<IReportTrain>(
                    "report-eu",
                    new ReportInput { Region = "eu" },
                    Every.Seconds(30)
                )
                .Schedule<IReportTrain>(
                    "report-us-nightly",
                    new ReportInput { Region = "us" },
                    Cron.Daily(hour: 3),
                    options => options.MaxRetries(5)
                )
        )
);

var app = builder.Build();
app.Run();
```

Stop the host, run it again, and repeat the first query. There are two manifests now:
`report-us-nightly` has a cron expression and a `next_scheduled_run` of the next 03:00 UTC. A cron
job never runs at startup, so the log still shows only the `eu` report. Restarting did not
duplicate `report-eu`: each start writes the manifests by external id, updating the ones that
exist.

## 6. Run the nightly job now

A host can trigger any manifest through `ITraxScheduler`. Map an endpoint that does it, in
Development only, since anyone who can reach it can run the job.

`Program.cs`:

```csharp compile=trigger
using Reports;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .Schedule<IReportTrain>(
                    "report-eu",
                    new ReportInput { Region = "eu" },
                    Every.Seconds(30)
                )
                .Schedule<IReportTrain>(
                    "report-us-nightly",
                    new ReportInput { Region = "us" },
                    Cron.Daily(hour: 3),
                    options => options.MaxRetries(5)
                )
        )
);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapPost(
        "/reports/{externalId}/run",
        async (string externalId, ITraxScheduler scheduler) =>
        {
            await scheduler.TriggerAsync(externalId);
            return Results.Accepted();
        }
    );

app.Run();
```

```bash
dotnet run
curl -X POST http://localhost:5000/reports/report-us-nightly/run
```

Use the port `dotnet run` prints. The request answers `202`, and within a couple of seconds the log
shows `Building the us report`. The trigger queued one run of the manifest and left its schedule
alone, so it still runs at 03:00.

## 7. Quiet the scheduler's own logs

The log also has a few lines from the scheduler every cycle. Raise their category in
`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Trax.Scheduler.Trains": "Warning"
    }
  }
}
```

Run the host again. The log now shows the reports and little else. Failures still come through: a
failed job is logged at `Error`, and a job that runs out of retries at `Warning`.

## Where next

- [How the scheduler decides when a manifest runs](/docs/scheduler/scheduling-options): intervals,
  cron, misfires, variance and what a restart rewrites.
- [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup): what to do with a job that ran
  out of retries.
- [Administrative trains](/docs/scheduler/admin-trains): the services this host started.
- [Startup errors](/docs/scheduler/startup-errors): what stops a scheduler host from starting.
- [Scheduler namespaces](/docs/scheduler/namespaces): where each type used here lives.
- The [Scheduling sample](/docs/samples/scheduling) is a whole scheduler host, with the dashboard
  and the GraphQL operations surface.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [TriggerAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler)
