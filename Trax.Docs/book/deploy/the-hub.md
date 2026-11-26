---
title: "19.3 The hub"
description: "Chapter 19 of the Trax book, section 3: The hub."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/deploy/migrate-the-shops-schema
---

# 19.3 The hub

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
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Scheduling;
using Trax.StateMachine.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");
var development = builder.Environment.IsDevelopment();

// The key the hub and the workers sign their events with. A process drops any event not signed
// with it. appsettings.Development.json holds a key printed in the book, for Development only.
var broadcastKey = Convert.FromBase64String(
    builder.Configuration["Broadcast:SigningKey"]
        ?? throw new InvalidOperationException("Broadcast:SigningKey is not set.")
);

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
                // A deploy's migrate step brings the shop's schema up to date too.
                .MigrateOnDeploy<ShopfrontDbContext>()
                // What the worker runs reaches the hub's subscriptions, and the other way round.
                // The broker is ConnectionStrings:RabbitMq in Development; deployed, its host, user
                // and password come apart, as RabbitMq:Host, RabbitMq:UserName and
                // RabbitMq:Password, so no password sits inside a URI.
                .UseBroadcaster(broadcaster =>
                    broadcaster.UseRabbitMq(
                        builder.Configuration,
                        rabbit =>
                        {
                            rabbit.SigningKey = broadcastKey;
                            // Set where trax deploy runs the broker beside the hub, reached by name
                            // on the app's own network. Anything but the text true keeps plain
                            // amqp:// refused; a broker anywhere else is reached over amqps://.
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
        // Checkout and the parcel are classes in the library. Returns are derived from
        // features/ by trax machine from-gherkin, which writes machines/from-features; the
        // build copies it to machines/ beside the hub.
        .AddStateMachines(
            machines => machines.ServeFeatures("machines", typeof(ManifestNames).Assembly),
            typeof(ManifestNames).Assembly
        )
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
    !development
    || builder.Configuration.GetValue("PersistedOperations:Require", defaultValue: true);

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

// In Development the hub migrates the shop's schema before it serves. Deployed, the deploy's
// migrate step does it (MigrateOnDeploy), once, before any process of the new version starts.
// An export opens no database.
if (development && !TraxClientExport.Requested)
{
    var scope = app.Services.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var factory = scope.ServiceProvider.GetRequiredService<
            IDbContextFactory<ShopfrontDbContext>
        >();
        var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Database.MigrateAsync().ConfigureAwait(false);
        }
    }
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
            (string user, TimeProvider clock, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer, clock)
        )
        .AllowAnonymous();

    // Takes this process's fake supplier down, or brings it back.
    app.MapPost(
            "/dev/supplier/{state}",
            (string state, FakeSupplier supplier) =>
            {
                supplier.IsDown = string.Equals(state, "down", StringComparison.Ordinal);
                return Results.NoContent();
            }
        )
        .AllowAnonymous();
}

// Serves; or writes the client manifest (--trax-export-client), reports what the hub needs from
// its deployment (--trax-export-deploy), migrates (--trax-migrate), or drains and resumes the
// hosts (--trax-drain, --trax-resume), and exits.
await app.RunTraxAsync(args).ConfigureAwait(false);
```

What changed:

- **The shop's schema is migrated, not created.** In Development the hub calls `MigrateAsync()` in
  place of `EnsureSchemaCreatedAsync` before it serves, as `dotnet run` and the tests start it.
  Deployed, it does not: `MigrateOnDeploy<ShopfrontDbContext>()` names the context for the deploy's
  migrate step, which starts the new version's image once with `--trax-migrate`, migrates Trax's
  tables and the shop's, and exits before any process of that version starts (19.7). Trax's own
  migrations run in that step too, and every serving process skips them.
- **`UseBroadcaster(b => b.UseRabbitMq(builder.Configuration, ...))`.** Until now the hub's
  subscriptions heard only about runs the hub executed itself. `runProgress` for a queued run, and
  the console's live views of the worker's runs, need the worker to tell the hub, and RabbitMQ
  carries it. A machine's changes do not need it: they travel through Postgres. A hub that cannot
  reach the broker still starts, and tries again until it can. It reads the broker from
  configuration: `ConnectionStrings:RabbitMq`, a URI, in Development, and deployed
  `RabbitMq:Host`, `RabbitMq:UserName` and `RabbitMq:Password` apart, so a generated password with
  a `/` or an `@` in it reaches the broker as it is, which it would not inside a URI.
- **A signing key, `Broadcast:SigningKey`.** Every process on the exchange signs the events it
  publishes with it and drops any event not signed with it, so a process that can reach the broker
  but does not hold the key cannot put events in front of the hub's subscribers. `UseRabbitMq`
  refuses to start without one. `appsettings.Development.json` holds a key printed here, which is
  no secret and is read in Development only; anywhere else the hub stops at start until the key is
  configured.
- **`AllowInsecureTransport`, from configuration.** `UseRabbitMq` refuses a plain `amqp://` broker
  that is not on this machine, since its credentials and every event would cross the network in
  cleartext. `localhost` passes. The deployment sets `Broadcast:AllowInsecureTransport` for the
  broker `trax deploy` runs beside the hub on the app's own network (19.6); a broker anywhere else
  is reached over `amqps://`. The hub reads it as the text `true` and nothing else, so a value that
  is not a boolean keeps cleartext refused rather than stopping the hub.
- **Two health checks.** `AddTraxSchedulerLiveness` fails when the scheduler has stopped
  dispatching, which is what a restart fixes: that is liveness. `AddTraxHealthCheck` reads the
  database and reports `Degraded` when dead letters wait for someone or runs keep failing: that is
  readiness, and what an alert watches. Both are anonymous, and say nothing a caller could use.
- **`RunTraxAsync`** already ended the hub for `trax client`. It also answers each step of a
  deploy: `--trax-export-deploy` reports what the hub needs from its deployment, `--trax-migrate`
  migrates, and `--trax-drain` and `--trax-resume` hold and release every host's new work around
  a release. Each does its step and exits without serving.
- **The connection strings moved to `appsettings.Development.json`.** Deployed, they come from the
  environment, and a broker URI left in `appsettings.json` would win over the broker's parts.

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
    <!-- The machines trax machine from-gherkin derives from ../features, as committed. The hub
         serves them from machines/ beside it, and refuses to start if they disagree. -->
    <None
      Include="..\machines\from-features\*"
      LinkBase="machines"
      CopyToOutputDirectory="PreserveNewest"
    />
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

`Shopfront.Hub/appsettings.Development.json`:

```json file=Shopfront.Hub/appsettings.Development.json
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

Next: [19.4 The worker](/docs/book/deploy/the-worker)
