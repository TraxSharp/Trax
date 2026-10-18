---
title: "9. Scheduled work"
description: "Chapter 9 of the Trax book: schedule a nightly stock report and a low-stock check after it, skip the report on holidays, and run both from the console."
type: tutorial
area: general
parent: The Trax book
nav_order: 19
snippet_context: none
follows: book/the-operations-console
---

# 9. Scheduled work

Everything Shopfront has done so far, it did because someone asked. In this chapter it starts doing
work nobody asks for. Every night it counts the stock on the shelves, and once the count is in, it
tells staff which products are running low. On the shop's public holidays the count is skipped, and
skipped means skipped: it does not run late the next morning instead.

This chapter starts from the end of [chapter 8](/docs/book/the-operations-console).

## A schedule is a manifest

The scheduler runs trains on a schedule. Each scheduled train is a **manifest**: the train, the
input it runs with, when it runs, and what to do when it fails. A manifest is identified by an
external id that the host chooses, so the same manifest is found again on every start and by every
process that shares the database, however many times the host is deployed.

Those ids are written once, as constants, at the root of the project.

`ManifestNames.cs`:

```csharp compile=app file=ManifestNames.cs
namespace Shopfront;

// The external id of each manifest Shopfront schedules: its identity on every start and every host.
public static class ManifestNames
{
    public const string StockReport = "stock-report";
    public const string LowStockCheck = "low-stock-check";
}
```

## The stock report

The report is a train like any other. Its output is the report: the time it was taken and each
product's stock. The run's record keeps that output, so the report needs no table of its own.

By the end of the chapter the new trains are:

```text
Trains/
├── StockReport/
│   ├── StockReportInput.cs
│   ├── StockReportOutput.cs
│   ├── IStockReportTrain.cs
│   ├── StockReportTrain.cs
│   ├── Junctions/
│   │   └── CountStockJunction.cs
│   └── Models/
│       └── StockLine.cs
└── LowStockCheck/
    ├── LowStockCheckInput.cs
    ├── LowStockCheckOutput.cs
    ├── ILowStockCheckTrain.cs
    ├── LowStockCheckTrain.cs
    └── Junctions/
        ├── FindLowStockJunction.cs
        └── NotifyStaffJunction.cs
```

A scheduled train's input is stored with its manifest, so it implements `IManifestProperties`, the
marker Trax uses to tell an input it may store from any other object. The report needs no
settings, so its input is empty.

`Trains/StockReport/StockReportInput.cs`:

```csharp compile=app file=Trains/StockReport/StockReportInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.StockReport;

public record StockReportInput : IManifestProperties;
```

`Trains/StockReport/Models/StockLine.cs`:

```csharp compile=app file=Trains/StockReport/Models/StockLine.cs
namespace Shopfront.Trains.StockReport.Models;

public record StockLine(string Sku, int Stock);
```

`StockLine` is part of the output, so it is public.

`Trains/StockReport/StockReportOutput.cs`:

```csharp compile=app file=Trains/StockReport/StockReportOutput.cs
using Shopfront.Trains.StockReport.Models;

namespace Shopfront.Trains.StockReport;

public record StockReportOutput(DateTime TakenAt, int Units, List<StockLine> Lines);
```

`Trains/StockReport/IStockReportTrain.cs`:

```csharp compile=app file=Trains/StockReport/IStockReportTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public interface IStockReportTrain : IServiceTrain<StockReportInput, StockReportOutput>;
```

`Trains/StockReport/Junctions/CountStockJunction.cs`:

```csharp compile=app file=Trains/StockReport/Junctions/CountStockJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.StockReport.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.StockReport.Junctions;

internal class CountStockJunction(IShopfrontDbContext db, ILogger<CountStockJunction> logger)
    : EffectJunction<StockReportInput, StockReportOutput>
{
    public override async Task<StockReportOutput> Run(StockReportInput input)
    {
        var lines = await db
            .Products.OrderBy(p => p.Sku)
            .Select(p => new StockLine(p.Sku, p.Stock))
            .ToListAsync(CancellationToken);

        var report = new StockReportOutput(DateTime.UtcNow, lines.Sum(l => l.Stock), lines);
        logger.LogInformation(
            "Counted {Units} units across {Products} products",
            report.Units,
            lines.Count
        );
        return report;
    }
}
```

`Trains/StockReport/StockReportTrain.cs`:

```csharp compile=app file=Trains/StockReport/StockReportTrain.cs
using Shopfront.Trains.StockReport.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public class StockReportTrain : ServiceTrain<StockReportInput, StockReportOutput>, IStockReportTrain
{
    protected override Task<Either<Exception, StockReportOutput>> Junctions() =>
        Chain<CountStockJunction>().Resolve();
}
```

The train carries no `[TraxAuthorize]` and no GraphQL attribute. Nobody asks for it: the scheduler
runs it, as trusted infrastructure, and it is not part of the API.

A scheduled run has no caller. The products have no owner, so the report reads them all; a train
that read orders would see none, because the query filter from chapter 6 shows a context with no
caller nothing. That is the filter failing closed, and it is why the report counts products rather
than adding up orders.

## The low-stock check

Staff hear about low stock through whatever the shop uses to reach them. Shopfront logs it for now,
behind an interface, so a real channel replaces one class later.

`Staff/IStaffNotifier.cs`:

```csharp compile=app file=Staff/IStaffNotifier.cs
namespace Shopfront.Staff;

public interface IStaffNotifier
{
    Task Notify(string message, CancellationToken cancellationToken);
}
```

`Staff/LoggingStaffNotifier.cs`:

```csharp compile=app file=Staff/LoggingStaffNotifier.cs
namespace Shopfront.Staff;

// Writes each message to the log. A real shop sends it to a channel staff read.
public class LoggingStaffNotifier(ILogger<LoggingStaffNotifier> logger) : IStaffNotifier
{
    public Task Notify(string message, CancellationToken cancellationToken)
    {
        logger.LogWarning("For staff: {Message}", message);
        return Task.CompletedTask;
    }
}
```

The check's input carries the one setting it has: how few is too few. Being stored with the
manifest, it can be changed without a release.

`Trains/LowStockCheck/LowStockCheckInput.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.LowStockCheck;

public record LowStockCheckInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
```

`Trains/LowStockCheck/LowStockCheckOutput.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckOutput.cs
namespace Shopfront.Trains.LowStockCheck;

public record LowStockCheckOutput(List<string> LowSkus);
```

`Trains/LowStockCheck/ILowStockCheckTrain.cs`:

```csharp compile=app file=Trains/LowStockCheck/ILowStockCheckTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public interface ILowStockCheckTrain : IServiceTrain<LowStockCheckInput, LowStockCheckOutput>;
```

`Trains/LowStockCheck/Junctions/FindLowStockJunction.cs`:

```csharp compile=app file=Trains/LowStockCheck/Junctions/FindLowStockJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class FindLowStockJunction(IShopfrontDbContext db)
    : EffectJunction<LowStockCheckInput, LowStockCheckOutput>
{
    public override async Task<LowStockCheckOutput> Run(LowStockCheckInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
```

`Trains/LowStockCheck/Junctions/NotifyStaffJunction.cs`:

```csharp compile=app file=Trains/LowStockCheck/Junctions/NotifyStaffJunction.cs
using Shopfront.Staff;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class NotifyStaffJunction(IStaffNotifier staff) : EffectJunction<LowStockCheckOutput, Unit>
{
    public override async Task<Unit> Run(LowStockCheckOutput low)
    {
        if (low.LowSkus.Count > 0)
            await staff.Notify($"Low on {string.Join(", ", low.LowSkus)}.", CancellationToken);
        return Unit.Default;
    }
}
```

`Trains/LowStockCheck/LowStockCheckTrain.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckTrain.cs
using Shopfront.Trains.LowStockCheck.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public class LowStockCheckTrain
    : ServiceTrain<LowStockCheckInput, LowStockCheckOutput>,
        ILowStockCheckTrain
{
    protected override Task<Either<Exception, LowStockCheckOutput>> Junctions() =>
        Chain<FindLowStockJunction>().Chain<NotifyStaffJunction>().Resolve();
}
```

`NotifyStaffJunction` returns `Unit`, so Memory still holds the `LowStockCheckOutput` the first
junction made when the chain resolves, and that is the train's output.

The notifier is registered with the rest of the shop.

`Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString),
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
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>();
}
```

## Schedule them

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

## Run it now

A new daily manifest first runs at its next 02:00, not when the host starts. Start the application,
and trigger the report as a member of staff instead of waiting:

```bash
dotnet run --urls http://localhost:5080
```

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"mutation { operations { triggerManifest(externalId: \"stock-report\") { success message } } }"}'
```

```json
{"data":{"operations":{"triggerManifest":{"success":true,"message":"Manifest 'stock-report' triggered: queued a new run (work queue entry 1), due now."}}}}
```

In the console, the same is **Manifests**, then **stock-report**, then **Run now**. Within a few
seconds the report has run, and the check after it:

```text
info: Shopfront.Trains.StockReport.Junctions.CountStockJunction[0]
      Counted 13 units across 3 products
...
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Low on CAP, TEE.
```

Your counts are lower, after the orders of the earlier chapters.

Open the report's run in the console's **Executions**: its **Output** is the report, every product
with its stock. **Manifests** lists both, with the cron expression, and the check's parent.

**Executions** also has a run every few seconds that Shopfront never wrote: the scheduler's own
trains, the manifest manager that decides what is due and the dispatcher that hands it to a
worker. They are recorded like any train. The console hides them while **Hide admin trains** is
checked.

## Test it

The junctions are tested against the test database, as the others are.

`tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs
using Shopfront.Staff;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.LowStockCheck.Junctions;
using Shopfront.Trains.StockReport;
using Shopfront.Trains.StockReport.Junctions;
using Shopfront.Trains.StockReport.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class StockJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private sealed class RecordingNotifier : IStaffNotifier
    {
        public List<string> Messages { get; } = [];

        public Task Notify(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task CountStock_CountsEveryProduct()
    {
        await using var db = TestDatabase.NewContext();

        var report = await new CountStockJunction(db, NullLogger<CountStockJunction>.Instance).Run(
            new StockReportInput()
        );

        report.Lines.Should().Equal(new StockLine("CAP", 0), new StockLine("MUG", 10), new StockLine("TEE", 3));
        report.Units.Should().Be(13);
    }

    [Test]
    public async Task FindLowStock_ListsProductsAtOrBelowTheThreshold()
    {
        await using var db = TestDatabase.NewContext();

        var low = await new FindLowStockJunction(db).Run(new LowStockCheckInput { Threshold = 3 });

        low.LowSkus.Should().Equal("CAP", "TEE");
    }

    [Test]
    public async Task NotifyStaff_NamesTheLowProducts()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput(["CAP", "TEE"]));

        staff.Messages.Should().Equal("Low on CAP, TEE.");
    }

    [Test]
    public async Task NotifyStaff_NothingLow_SaysNothing()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput([]));

        staff.Messages.Should().BeEmpty();
    }
}
```

The host test checks what the host declares: both manifests, the check after the report.

`tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ScheduleTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    [Test]
    public async Task Manifests_TheReportAndTheCheckAreScheduled()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("lin", Roles.Staff)
        );

        var response = await client.Post(
            "{ operations { manifests(take: 10) { items { externalId cronExpression } } } }"
        );

        var manifests = response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("manifests")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("externalId").GetString()!,
                m => m.GetProperty("cronExpression").GetString()
            );
        manifests.Should().ContainKey(ManifestNames.StockReport).WhoseValue.Should().Be("0 2 * * *");
        manifests.Should().ContainKey(ManifestNames.LowStockCheck);
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60, Duration: 55 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront counts its stock every night and tells staff what is running low, without anyone asking,
and leaves the shop's holidays alone. Staff can see each schedule and each run in the console, and
run either now.

Each of those runs succeeded. The next chapter adds work that fails: a supplier's stock feed that
is sometimes down, retried, given up on, and picked up again by staff.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)
