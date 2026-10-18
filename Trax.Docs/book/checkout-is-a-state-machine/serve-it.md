---
title: "12.6 Serve it"
description: "Chapter 12 of the Trax book, section 6: Serve it."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/checkout-is-a-state-machine/the-machine
---

# 12.6 Serve it

Mark a machine `[TraxMachine]` and the hub serves it as typed GraphQL: `start`, one mutation per
event, `get`, and a subscription that pushes every change. The server applies every event; the
browser only asks.

A served machine's operations are persisted documents the host generates, and a host that serves
one must run persisted operations. Shopfront turns them on now, in **shadow mode**: every document
anyone sends still runs, and each one the host does not hold as a persisted document is logged.
That is the first step of turning them on, and it stays until the storefront exists to say which
documents it sends.

Instances are owned by a user key, which the host works out from the signed-in caller. The hub
says how, in one class.

`Shopfront.Hub/Auth/TraxCallerSnapshotPrincipal.cs`:

```csharp compile=app file=Shopfront.Hub/Auth/TraxCallerSnapshotPrincipal.cs
using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Auth;

// A machine instance belongs to the signed-in caller, by their qualified id.
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

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
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
        // Shadow mode: everything runs, and what would be refused is logged.
        .UsePersistedOperations(persisted =>
            persisted.SingleNode().RequirePersisted(false).LogNonPersistedRequests(true)
        )
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

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

await app.RunAsync();
```

What changed:

- **`AddStateMachines(...)`** finds the library's machines and stores their instances. It comes
  after `AddEffects` and before `AddMediator`.
- **`AddJunctionProgress()`** lets a run be stopped between junctions, which a machine needs once
  it waits on a run.
- **`ISnapshotPrincipal`** is the class above.
- **`UsePersistedOperations(...)`** turns on persisted operations, in shadow mode, on one node.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.7 Check out](/docs/book/checkout-is-a-state-machine/check-out)
