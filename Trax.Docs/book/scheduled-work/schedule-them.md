---
title: "9.4 Schedule them"
description: "Chapter 9 of the Trax book, section 4: Schedule them."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/scheduled-work/the-low-stock-check
---

# 9.4 Schedule them

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.StockReport;
using Trax.Api.Auth.Jwt;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddShopfront(connectionString);

if (builder.Environment.IsDevelopment())
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

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
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
        )
);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

What the schedule says, line by line:

- **`Schedule<IStockReportTrain>`** names the train by its interface, the manifest by its external
  id, the input it runs with, and when.
- **`Cron.Daily(hour: 2)`** is two in the morning, in UTC. Every schedule is in UTC, so a shop in
  another time zone converts once, here.
- **`Exclude.Dates(...)`** skips the listed days. The dates are UTC dates too.
- **`OnMisfire(MisfirePolicy.DoNothing)`** is what makes the skip stick. A run is a misfire when its
  time passes without it, and the default policy, `FireOnceNow`, runs a missed one as soon as it
  can. A holiday's 02:00 run passes without running by design, so with the default it would run
  at midnight, when the holiday ends. `DoNothing` drops it and waits for the next 02:00.
- **`Variance(TimeSpan.FromMinutes(10))`** moves each run up to ten minutes later than 02:00, at
  random, so several shops on one database do not all count at once.
- **`ThenInclude<ILowStockCheckTrain>`** schedules the check as a dependent: it runs after each
  report that succeeds, and not after one that fails or is skipped. It has no schedule of its own.

The schedule is declared in code and written to the database on every start: a manifest whose
external id exists is updated, and a new one is created. A setting the code does not state, such
as one an operator changed in the console, is left as the operator set it.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.5 Run it now](/docs/book/scheduled-work/run-it-now)
