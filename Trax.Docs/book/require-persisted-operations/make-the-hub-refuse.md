---
title: "17.2 Make the hub refuse"
description: "Chapter 17 of the Trax book, section 2: Make the hub refuse."
type: tutorial
area: general
parent: "17. Require persisted operations"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/require-persisted-operations/what-the-hub-registers
---

# 17.2 Make the hub refuse

`Shopfront.Hub/Program.cs`:

```csharp compile=app file=Shopfront.Hub/Program.cs
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
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
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

var app = builder.Build();

// An export of the client manifest opens no database.
if (!TraxClientExport.Requested)
    await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

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

- **`RequirePersisted(requirePersisted)`** is on wherever the hub runs. A document sent as text is
  refused, on HTTP and on the socket, with `PERSISTED_OPERATION_REQUIRED`.
- **`LogNonPersistedRequests`** goes with shadow mode, so it is on only when text may run.
- **`DisableIntrospection()`.** Introspection is a document written as text as well, the one a
  tool sends to read the schema, and a host that runs only its own documents has no caller who
  needs it. `trax client` reads the manifest, not the schema.
- **`PersistedOperations:Require`** can turn the refusal off, in Development only. Outside
  Development the hub ignores it, so a deployed hub cannot be configured into accepting text. It
  is there for the tests and for exploring by hand with `curl`.

## SDK Reference

> [Turn on persisted operations](/docs/api/turn-on-persisted-operations) | [Persisted operations](/docs/api/persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)

---

Next: [17.3 What a caller sees](/docs/book/require-persisted-operations/what-a-caller-sees)
