---
title: "18.3 The hub"
description: "Chapter 18 of the Trax book, section 3: The hub."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/deploy/migrate-the-shops-schema
---

# 18.3 The hub

`Shopfront.Hub/Program.cs`:

```csharp compile=app file=Shopfront.Hub/Program.cs
using Microsoft.EntityFrameworkCore;
using Shopfront;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.Restock;
using Shopfront.Trains.StockReport;
using Trax.Api.Auth.Jwt;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");
var rabbitMq =
    builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not set.");
var development = builder.Environment.IsDevelopment();

builder.Services.AddShopfront(connectionString);

if (development)
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(DevJwt.Issuer, DevJwt.Audience, DevJwt.Key)
    );
else
    builder.Services.AddTraxJwtAuth(
        builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is not set."),
        builder.Configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is not set.")
    );
builder.Services.AddAuthorization();

// The storefront is served from another origin, which the browser asks the hub to allow.
builder.Services.AddCors(cors =>
    cors.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                builder.Configuration["Storefront:Origin"]
                    ?? throw new InvalidOperationException("Storefront:Origin is not set.")
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
    )
);
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                // What the worker runs reaches the hub's subscriptions, and the other way round.
                .UseBroadcaster(broadcaster => broadcaster.UseRabbitMq(rabbitMq))
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddJunctionProgress()
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddStateMachines(typeof(ManifestNames).Assembly)
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
        {
            // The hub hands every job to the worker through Postgres and runs none itself.
            scheduler.OverrideSubmitter(services =>
                services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
            );

            // In Development, retries come seconds apart, and a run whose worker died is given up
            // on after a minute rather than an hour, to be watched.
            if (development)
                scheduler
                    .DefaultRetryDelay(TimeSpan.FromSeconds(5))
                    .MaxRetryDelay(TimeSpan.FromSeconds(30))
                    .DefaultJobTimeout(TimeSpan.FromSeconds(30))
                    .StaleInProgressTimeout(TimeSpan.FromMinutes(1));

            return scheduler
                .Schedule<IStockReportTrain>(
                    ManifestNames.StockReport,
                    new StockReportInput(),
                    Cron.Daily(hour: 2),
                    options =>
                        options
                            // The shop's holidays: no count, and none the morning after.
                            .Exclude(
                                Exclude.Dates(new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1))
                            )
                            .OnMisfire(MisfirePolicy.DoNothing)
                            .Variance(TimeSpan.FromMinutes(10))
                )
                .ThenInclude<ILowStockCheckTrain>(
                    ManifestNames.LowStockCheck,
                    new LowStockCheckInput { Threshold = 3 }
                )
                .Schedule<IRestockTrain>(
                    ManifestNames.Restock,
                    new RestockInput { Threshold = 3 },
                    development ? Every.Minutes(1) : Every.Hours(1),
                    options => options.MaxRetries(2)
                );
        })
);

// Only registered documents run. PersistedOperations:Require=false lets documents written as text
// run too, for the tests and for exploring by hand, and is read in Development only: anywhere else
// the setting is ignored.
var requirePersisted =
    !development || builder.Configuration.GetValue("PersistedOperations:Require", true);

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
        .UsePersistedOperations(persisted =>
        {
            persisted
                .SingleNode()
                .RequirePersisted(requirePersisted)
                .LogNonPersistedRequests(!requirePersisted)
                // The storefront's documents, which trax client writes and the build copies here.
                .ClientDocuments(Path.Combine(AppContext.BaseDirectory, "trax.documents.json"));
            // Introspection is a document written as text too.
            if (requirePersisted)
                persisted.DisableIntrospection();
        })
);

// Liveness: the scheduler is still dispatching. Readiness: the database answers, and how the
// runs are going.
builder
    .Services.AddHealthChecks()
    .AddTraxSchedulerLiveness(tags: ["live"])
    .AddTraxHealthCheck(tags: "ready");

var app = builder.Build();

// The shop's schema is migrated before the hub serves. An export opens no database.
if (!TraxClientExport.Requested)
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());
app.MapHealthChecks("/health/live", new() { Predicate = check => check.Tags.Contains("live") })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

if (development)
{
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

    // Takes this process's fake supplier down, or brings it back.
    app.MapPost(
            "/dev/supplier/{state}",
            (string state, FakeSupplier supplier) =>
            {
                supplier.IsDown = state == "down";
                return Results.NoContent();
            }
        )
        .AllowAnonymous();
}

// Serves, or with --trax-export-client writes the client manifest and exits.
await app.RunTraxAsync(args);
```

What changed:

- **`MigrateAsync()`** in place of `EnsureSchemaCreatedAsync`, before the hub serves. The hub is the
  one process that migrates the shop's schema; the workers start after it. Trax's own migrations
  run in every process, one after another, under a lock.
- **`UseBroadcaster(b => b.UseRabbitMq(rabbitMq))`.** Until now the hub's subscriptions heard only
  about runs the hub executed itself. `runProgress` for a queued run, and the console's live views
  of the worker's runs, need the worker to tell the hub, and RabbitMQ carries it. A machine's
  changes do not need it: they travel through Postgres. A hub that cannot reach the broker still
  starts, and tries again until it can.
- **Two health checks.** `AddTraxSchedulerLiveness` fails when the scheduler has stopped
  dispatching, which is what a restart fixes: that is liveness. `AddTraxHealthCheck` reads the
  database and reports `Degraded` when dead letters wait for someone or runs keep failing: that is
  readiness, and what an alert watches. Both are anonymous, and say nothing a caller could use.

`Shopfront.Hub/Shopfront.Hub.csproj`:

```xml file=Shopfront.Hub/Shopfront.Hub.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The host tests start the hub. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <!-- The storefront's documents, registered at start. trax client writes the file. -->
    <None
      Include="..\web\src\trax\trax.documents.json"
      Link="trax.documents.json"
      CopyToOutputDirectory="PreserveNewest"
      Condition="Exists('..\web\src\trax\trax.documents.json')"
    />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Api.Auth.Jwt" />
    <PackageReference Include="Trax.Api.Dashboard" />
    <PackageReference Include="Trax.Api.GraphQL" />
    <PackageReference Include="Trax.Api.GraphQL.PersistedOperations" />
    <PackageReference Include="Trax.Effect.Broadcaster.RabbitMQ" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

`Shopfront.Hub/appsettings.json`:

```json file=Shopfront.Hub/appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider": "Debug"
    }
  },
  "Storefront": {
    "Origin": "http://localhost:5173"
  },
  "AllowedHosts": "*"
}
```

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.4 The worker](/docs/book/deploy/the-worker)
