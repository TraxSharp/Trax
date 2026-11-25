---
title: "19.4 The worker"
description: "Chapter 19 of the Trax book, section 4: The worker."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/deploy/the-hub
---

# 19.4 The worker

`Shopfront.Worker/Program.cs`:

```csharp compile=worker file=Shopfront.Worker/Program.cs
using Shopfront;
using Shopfront.Extensions;
using Shopfront.Staff;
using Trax.Api.Auth;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

// The key the hub and the workers sign their events with. A process drops any event not signed
// with it. appsettings.Development.json holds a key printed in the book, for Development only.
var broadcastKey = Convert.FromBase64String(
    builder.Configuration["Broadcast:SigningKey"]
        ?? throw new InvalidOperationException("Broadcast:SigningKey is not set.")
);

builder.Services.AddShopfront(connectionString);

// The trains ask who the caller is. On the worker nobody is, and that is the answer they get.
builder.Services.AddTraxPrincipalAccessor();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                // The runs this worker executes reach the hub's subscriptions over RabbitMQ, read
                // from configuration as the hub reads it.
                .UseBroadcaster(broadcaster =>
                    broadcaster.UseRabbitMq(
                        builder.Configuration,
                        rabbit =>
                        {
                            rabbit.SigningKey = broadcastKey;
                            // As on the hub: plain amqp:// only where the deployment says so.
                            rabbit.AllowInsecureTransport = string.Equals(
                                builder.Configuration["Broadcast:AllowInsecureTransport"],
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            );
                        }
                    )
                )
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddJunctionProgress()
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                // The hub authorized every job this worker runs before it was queued.
                .AllowMissingAuthorizationService()
        )
);
builder.Services.AddTraxWorker(worker => worker.WorkerCount = 2);

// The process is up. Trax has no check of its own for a worker.
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health/live");

// Serves; or reports what the worker needs from its deployment (--trax-export-deploy) and exits.
await app.RunTraxAsync(args).ConfigureAwait(false);
```

The worker publishes the events of every run it executes, signed with the same
`Broadcast:SigningKey` as the hub, and reads the broker from configuration the same way. It ends
with `RunTraxAsync` in place of `app.RunAsync()`: started with `--trax-export-deploy`, it reports
what it needs from its deployment and exits, and a worker that called `app.RunAsync()` would serve
instead, so `trax deploy` could not ask it.

Its connection strings and key moved to `appsettings.Development.json`, as the hub's did. No launch
profile sets the worker's environment, so run it locally in Development:

```bash
dotnet run --project Shopfront.Worker --urls http://localhost:5081 --environment Development
```

Trax has no health check for a worker, so `/health/live` says only that the process answers. A
worker that is stopped finishes the run it is in, for up to 30 seconds, then gives up on it; a
worker that dies leaves its run for the hub to fail, as chapter 11 showed. A release does not stop
it mid-run: it drains first (19.7).

`Shopfront.Worker/Shopfront.Worker.csproj`:

```xml file=Shopfront.Worker/Shopfront.Worker.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Effect.Broadcaster.RabbitMQ" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

`Shopfront.Worker/appsettings.json`:

```json file=Shopfront.Worker/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

`Shopfront.Worker/appsettings.Development.json`:

```json file=Shopfront.Worker/appsettings.Development.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  },
  "Broadcast": {
    "SigningKey": "c2hvcGZyb250LWRldmVsb3BtZW50LWJyb2FkY2FzdC1rZXktcHJpbnRlZC1pbi10aGUtYm9vaw=="
  }
}
```

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [19.5 The tests migrate too](/docs/book/deploy/the-tests-migrate-too)
