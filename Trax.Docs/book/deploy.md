---
title: "18. Deploy"
description: "Chapter 18 of the Trax book: run the hub and its workers in containers on one Postgres and RabbitMQ, with migrations and health checks."
type: tutorial
area: general
parent: The Trax book
nav_order: 28
snippet_context: none
follows: book/require-persisted-operations
---

# 18. Deploy

Shopfront has run on one machine, in Development, from `dotnet run`. In this chapter it runs as it
would deployed: a hub and two workers in containers, one Postgres, and RabbitMQ between them. Four
things change before it can: the shop's schema is migrated rather than created, the processes tell
each other about runs, each says whether it is healthy, and the hub runs in Production, which
turns off everything that was there for Development.

This chapter starts from the end of [chapter 17](/docs/book/require-persisted-operations).

## The shape

This is the [hub and distributed workers](/docs/reference/deployment-topologies) topology, which
chapter 11 set up on one machine:

```text
                 ┌──────────────┐
  storefront ───►│     hub      │──── schedules, serves GraphQL and the console, runs no job
  console    ───►│ (one)        │
                 └──────┬───────┘
                        │ jobs in Postgres          run events over RabbitMQ
              ┌─────────┴─────────┐ ◄──────────────────────────────────────┐
              ▼                   ▼                                        │
        ┌──────────┐        ┌──────────┐                                   │
        │ worker 1 │        │ worker 2 │ ── claim jobs, run them ───────────┘
        └──────────┘        └──────────┘
```

| Process | Registers | Must not |
|---|---|---|
| hub | `AddScheduler` with the Postgres submitter, `AddTraxGraphQL`, the console, the machines | run jobs itself |
| worker | `AddTraxWorker`, `AddMediator` over the same trains | `AddScheduler`, `AddTraxGraphQL` |

Workers claim jobs from Postgres, each job by exactly one worker, so a third worker is a third
container and nothing else.

## Migrate the shop's schema

Since chapter 4, `EnsureSchemaCreatedAsync` has created the shop's tables. It creates what is
missing and changes nothing that exists, which suits a schema that is still being written. A
deployed shop's schema changes under data, so it moves to EF Core migrations. Trax's own tables are
migrated separately, by `UsePostgres` when each process starts, and the two never touch each
other's history.

Add the EF tools, at the version of EF Core the rest of Shopfront uses:

```bash
dotnet tool install dotnet-ef --version 10.0.12
```

`Directory.Packages.props`:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL.PersistedOperations" Version="1.61.0" />
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Broadcaster.RabbitMQ" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Progress" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Persistence" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Scheduler" Version="1.61.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

`Microsoft.EntityFrameworkCore.Design` goes in the library that holds the context. `PrivateAssets`
keeps it out of everything that references the library: only the tools use it.

`Shopfront.Core/Shopfront.Core.csproj`:

```xml file=Shopfront.Core/Shopfront.Core.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- The two the web project's implicit usings gave the code that moved here. -->
    <Using Include="Microsoft.Extensions.DependencyInjection" />
    <Using Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Trax.Api.Auth" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.StateMachine.Persistence" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>

  <ItemGroup>
    <!-- Each machine's committed corpus, which it replays to check itself. -->
    <EmbeddedResource
      Include="..\machines\checkout\differential.json"
      LogicalName="machines/checkout/differential.json"
      Condition="Exists('..\machines\checkout\differential.json')"
    />
  </ItemGroup>
</Project>
```

The provider options the hub, the tests and the tools use are written once. They put the
migrations' history in the `shop` schema, beside the shop's tables.

`Shopfront.Core/Data/ShopfrontDatabase.cs`:

```csharp compile=app file=Shopfront.Core/Data/ShopfrontDatabase.cs
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Shopfront.Data;

// The provider options the hub, the tests and the EF tools share.
public static class ShopfrontDatabase
{
    // The migrations' history goes in the shop's own schema, beside its tables.
    public static void Npgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "shop");
}
```

`dotnet ef` builds the context from a design-time factory rather than starting the hub, which
would migrate Trax's tables and run the rest of `Program.cs`.

`Shopfront.Core/Data/ShopfrontDbContextFactory.cs`:

```csharp compile=app file=Shopfront.Core/Data/ShopfrontDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shopfront.Data;

// What dotnet ef builds the context from, instead of starting the hub. It reads the model and
// connects to nothing.
public class ShopfrontDbContextFactory : IDesignTimeDbContextFactory<ShopfrontDbContext>
{
    public ShopfrontDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=shopfront", ShopfrontDatabase.Npgsql)
                .Options
        );
}
```

`Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Couriers;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Warehouse;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself. Every process that runs the
    // trains calls it, because Trax checks every train's chain when a process starts.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString, ShopfrontDatabase.Npgsql),
                ServiceLifetime.Scoped
            )
            .AddScoped<IShopfrontDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>().CreateDbContext()
            )
            .AddScoped<IShopfrontCaller, TraxShopfrontCaller>()
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>()
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>())
            .AddSingleton<IWarehouse, FakeWarehouse>()
            .AddSingleton<ICourier, FakeCourier>();
}
```

Generate the first migration:

```bash
dotnet ef migrations add Initial --project Shopfront.Core --output-dir Data/Migrations
```

```text
Shopfront.Core/Data/Migrations/
├── 20261018214548_Initial.cs
├── 20261018214548_Initial.Designer.cs
└── ShopfrontDbContextModelSnapshot.cs
```

Commit them. Every later change to the model is a migration of its own, `dotnet ef migrations add`
with a name that says what changed.

Your Development database was created by `EnsureSchemaCreatedAsync`, so it has the tables and no
history, and the first migration would fail on tables that already exist. Drop it and let the hub
create it again, or [record the first migration as applied](/docs/effect/move-a-domain-data-context-to-ef-migrations#a-database-ensureschemacreatedasync-already-created).

## The hub

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

## The worker

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
var rabbitMq =
    builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not set.");

builder.Services.AddShopfront(connectionString);

// The trains ask who the caller is. On the worker nobody is, and that is the answer they get.
builder.Services.AddTraxPrincipalAccessor();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                // The runs this worker executes reach the hub's subscriptions over RabbitMQ.
                .UseBroadcaster(broadcaster => broadcaster.UseRabbitMq(rabbitMq))
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
await app.RunAsync();
```

The worker publishes the events of every run it executes. Trax has no health check for a worker,
so `/health/live` says only that the process answers. A worker that is stopped finishes the run it
is in, for up to 30 seconds, then gives up on it; a worker that dies leaves its run for the hub to
fail, as chapter 11 showed.

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
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## The tests migrate too

The test database is migrated as the hub's is, so the tests run against the schema the migrations
build, and a model change without a migration fails them.

`tests/Shopfront.Tests/TestDatabase.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/TestDatabase.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Tests;

// The tests' own database, beside the application's, so a test never touches its rows.
internal static class TestDatabase
{
    public const string ConnectionString =
        "Host=localhost;Database=shopfront_tests;Username=postgres;Password=shopfront";

    // A context for a caller; staff, who read every order, unless the test says otherwise.
    public static ShopfrontDbContext NewContext(IShopfrontCaller? caller = null) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql(ConnectionString, ShopfrontDatabase.Npgsql)
                .Options,
            caller ?? TestCaller.Staff
        );

    // Drops the database and migrates a new one, as the hub does: the seeded catalogue, and no
    // orders or runs.
    public static async Task Reset()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public static async Task<int> StockOf(string sku)
    {
        await using var db = NewContext();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Stock).SingleAsync();
    }
}
```

The architecture guards check the same in CI, without a database: a context listed in
`MigrationContexts` must match its latest migration.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;
using Trax.Effect.Data.Testing;
using Trax.Mediator.Testing;

namespace Shopfront.Tests;

[TestFixture]
public sealed class ShopfrontTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(IPlaceOrderTrain).Assembly];
}

[TestFixture]
public sealed class ShopfrontHygieneGuards : HygieneGuardFixture;

[TestFixture]
public sealed class ShopfrontDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options =>
        new() { SourceScanRoots = ["Shopfront.Core/Data"] };

    protected override IReadOnlyList<Type> DomainContexts => [typeof(ShopfrontDbContext)];

    // The contexts EF migrations keep: each model must match its latest migration.
    protected override IReadOnlyList<Type> MigrationContexts => [typeof(ShopfrontDbContext)];

    // The model, built with no database and no caller, for the census to read.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new ShopfrontDbContext(options, NoShopfrontCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Order),
            PrincipalAccessorType = typeof(IShopfrontCaller),
            OwnerIdProperties = new HashSet<string> { nameof(Order.CustomerId) },
        };
}
```

Start the hub and worker with RabbitMQ running, and both log that they joined it:

```text
info: Trax.Effect.Broadcaster.RabbitMQ.RabbitMqTrainEventReceiver[0]
      RabbitMQ receiver started on exchange trax.lifecycle, queue amq.gen-cr_Uh-wKacH-AdfGlPjXyA.
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    76, Skipped:     0, Total:    76 - Shopfront.Tests.dll (net10.0)
```

## Containers

One image per process, each built from the solution folder, since the hub and the worker both
reference the library. The SDK image builds and publishes; the runtime image runs, as a user that
is not root.

`Shopfront.Hub/Dockerfile`:

```dockerfile file=Shopfront.Hub/Dockerfile
# Built from the solution folder: docker build -f Shopfront.Hub/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The packages first, so a change to the code does not restore them again.
COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Hub/Shopfront.Hub.csproj Shopfront.Hub/
RUN dotnet restore Shopfront.Hub/Shopfront.Hub.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Hub/ Shopfront.Hub/
# The machine's corpus, which the library embeds, and the storefront's documents, which the hub
# registers at start.
COPY machines/ machines/
COPY web/src/trax/trax.documents.json web/src/trax/
RUN dotnet publish Shopfront.Hub/Shopfront.Hub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Hub.dll"]
```

`Shopfront.Worker/Dockerfile`:

```dockerfile file=Shopfront.Worker/Dockerfile
# Built from the solution folder: docker build -f Shopfront.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Worker/Shopfront.Worker.csproj Shopfront.Worker/
RUN dotnet restore Shopfront.Worker/Shopfront.Worker.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Worker/ Shopfront.Worker/
COPY machines/ machines/
RUN dotnet publish Shopfront.Worker/Shopfront.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Worker.dll"]
```

`.dockerignore`:

```text file=.dockerignore
**/bin/
**/obj/
**/node_modules/
web/dist/
```

The hub's image carries `trax.documents.json`, which it registers at start: the image is built
after `trax client`, from the same commit as the storefront.

## Run it as deployed

`compose.yaml` runs the whole of it: Postgres, RabbitMQ, the hub and two workers.

`compose.yaml`:

```yaml file=compose.yaml
# Shopfront as it runs deployed: one hub, two workers, Postgres and RabbitMQ. Every secret comes
# from the environment, or from a .env file beside this one that is never committed.

# What the hub and the workers both need.
x-shopfront: &shopfront
  ConnectionStrings__Shopfront: Host=database;Database=shopfront;Username=shopfront;Password=${POSTGRES_PASSWORD}
  ConnectionStrings__RabbitMq: amqp://shopfront:${RABBITMQ_PASSWORD}@rabbitmq:5672

services:
  database:
    image: postgres:17
    environment:
      POSTGRES_DB: shopfront
      POSTGRES_USER: shopfront
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}
    volumes:
      - database:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "shopfront"]
      interval: 5s

  rabbitmq:
    image: rabbitmq:4
    environment:
      RABBITMQ_DEFAULT_USER: shopfront
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s

  hub:
    build:
      context: .
      dockerfile: Shopfront.Hub/Dockerfile
    environment:
      <<: *shopfront
      # The identity provider whose tokens the hub accepts, and where the storefront is served.
      Auth__Authority: ${AUTH_AUTHORITY:?set AUTH_AUTHORITY}
      Auth__Audience: ${AUTH_AUDIENCE:?set AUTH_AUDIENCE}
      Storefront__Origin: ${STOREFRONT_ORIGIN:?set STOREFRONT_ORIGIN}
    ports:
      - "8080:8080"
    depends_on:
      database:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy

  worker:
    build:
      context: .
      dockerfile: Shopfront.Worker/Dockerfile
    environment: *shopfront
    deploy:
      replicas: 2
    # The hub migrates the shop's schema before it serves.
    depends_on:
      hub:
        condition: service_started

volumes:
  database:
```

A published build runs in Production unless its environment says otherwise, and nothing here says
otherwise. So:

- **The hub accepts only tokens from your identity provider**, `Auth:Authority` and
  `Auth:Audience`. The Development key is refused outside Development, and `/dev/token` is not
  mapped.
- **Only registered documents run**, and `PersistedOperations:Require` is ignored.
- **The GraphQL IDE and schema download are not served.**
- **Development's short timeouts and fast retries are off**: a run whose worker died is failed
  after more than an hour, not a minute, and the restock runs hourly.

Set the secrets, and start it:

```bash
export POSTGRES_PASSWORD=... RABBITMQ_PASSWORD=...
export AUTH_AUTHORITY=https://login.example.com/ AUTH_AUDIENCE=shopfront
export STOREFRONT_ORIGIN=https://shop.example.com
docker compose up -d --build
```

```bash
curl -s http://localhost:8080/health/live
curl -s http://localhost:8080/health/ready
```

```text
Healthy
Healthy
```

The hub migrated both schemas and serves:

```text
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261018214548_Initial'.
...
info: Trax.Effect.Broadcaster.RabbitMQ.RabbitMqTrainEventReceiver[0]
      RabbitMQ receiver started on exchange trax.lifecycle, queue amq.gen-iiJgCfj_9Vb3N9nM6CyCoA.
info: Trax.Scheduler.Services.SchedulerStartupService.SchedulerStartupService[0]
      Successfully seeded 3 manifest(s)
info: Trax.Api.GraphQL.PersistedOperations[0]
      Persisted-operation enforcement is ON. ...
```

EF Core logs one `fail:` before that, the first time: it looks for the history table before it
creates it. On a fresh database that is expected, and later starts log none.

The storefront's documents run by id, and text is refused, as in chapter 17. The catalogue has more
caps and T-shirts than it started with: the restock ran as soon as the hub scheduled it, on a
worker, which logged `Received 2 deliveries`.

```text
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":5},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":8}]}}}} 200
{"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]} 400
```

`/dev/token` and the GraphQL IDE answer `404`. The hub also warns that ASP.NET Core keeps its
data-protection keys inside the container. Shopfront signs nothing with them, since its callers
carry bearer tokens and no cookies; a hub that adds cookie sign-in keeps those keys somewhere that
outlives the container.

## The storefront

The storefront is static files. Build it with the hub's address, and serve `web/dist` from
wherever the shop's pages are served, at the origin `Storefront:Origin` names:

```bash
cd web
VITE_HUB_URL=https://api.shop.example.com npm run build
```

`web/src/connection.ts`:

```ts file=web/src/connection.ts
import { configureTrax } from "./trax/index.g";

// Where the hub is: from the build's environment, or the hub `dotnet run` starts.
const HUB = import.meta.env.VITE_HUB_URL ?? "http://localhost:5080";

// Signs in as a customer with a Development token from the hub, and points the generated client at
// it. Every request carries the token; the socket, which cannot carry a header, sends it when it
// opens. A deployed storefront gets its token from the identity provider the hub trusts instead.
export async function signIn(user: string): Promise<void> {
  const token = await (await fetch(`${HUB}/dev/token/${user}`)).text();
  configureTrax({
    url: `${HUB}/trax/graphql`,
    socketUrl: `${HUB.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${token}` }),
    connectionParams: () => ({ authToken: token }),
    user,
  });
}
```

`web/src/vite-env.d.ts`:

```ts file=web/src/vite-env.d.ts
/// <reference types="vite/client" />
```

A deployed storefront signs customers in with the identity provider the hub trusts, and hands the
token it gets to `configureTrax`, as the page does with the Development token now.

## The console

The console is served at `/trax/dashboard` with `AllowAnonymousShell()`, and the hub warns about it
every time it starts:

```text
warn: Trax.Api.Dashboard[0]
      AllowAnonymousShell() is set: the Trax API dashboard at /trax/dashboard applies no authorization of its own, so anyone who can reach it can load the app. What the app shows and changes is still authorized by the Trax GraphQL endpoint. Use RequirePolicy() or RequireRoles() unless something in front of it is the gate.
```

The shell is the page and its scripts. Everything it shows comes from the `operations` namespace,
which `GateOperations(roles: Roles.Staff)` refuses to anyone who is not staff, whoever loaded the
page. A page load cannot carry a bearer token, so a role on the shell itself needs a scheme the
browser sends, a cookie from OIDC sign-in. Until Shopfront has one, keep the shell anonymous and
put `/trax/dashboard` behind something that is the gate: an internal network, a VPN, or the
ingress's own sign-in.

## Before it takes real orders

- **HTTPS** in front of the hub, terminated at the ingress or the load balancer.
- **Secrets** from a secret store, not a file in the repository.
- **Fakes.** The payment provider, the courier, the supplier and the warehouse are fakes, registered
  in `AddShopfront`. Each is an interface, so a real one replaces it there, and nothing else
  changes.
- **A second hub** needs persisted-operation changes broadcast between hubs: replace `SingleNode()`
  with `UseRabbitMqInvalidation(rabbitMq)`. Only one hub runs the schedule at a time; the others
  serve.

## What we have

Shopfront, deployed: a hub that serves the storefront and the console, workers that run the jobs,
and the two sharing one Postgres and one RabbitMQ. That is the end of the book.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)
