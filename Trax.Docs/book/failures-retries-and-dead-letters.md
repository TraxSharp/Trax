---
title: "10. Failures, retries and dead letters"
description: "Chapter 10 of the Trax book: restock from a supplier that goes down, retry with backoff, and requeue the dead letter once the supplier is back."
type: tutorial
area: general
parent: The Trax book
nav_order: 20
snippet_context: none
follows: book/scheduled-work
---

# 10. Failures, retries and dead letters

The stock report and the low-stock check never fail: they read a table. In this chapter Shopfront
restocks from a supplier, which is somebody else's system and goes down. A restock that fails is
tried again a little later, then a little later still, and when it has failed enough times it
stops, is set aside as a dead letter, and staff are told. Once the supplier is back, a member of
staff sends it round again from the console.

This chapter starts from the end of [chapter 9](/docs/book/scheduled-work).

## What is retried

Trax retries a whole run, not a junction: a failed run of a manifest is run again from its first
junction. Only a manifest's runs are retried this way. A train run on a request, as `PlaceOrder`
is, fails once and reports it, because the customer waiting on it is the one who decides whether
to try again. Work nobody waits for is what the scheduler retries, so the restock is a manifest.

## The supplier

The supplier delivers stock for the products it is asked about. Ours is a fake that a developer can
take down and bring back.

`Suppliers/SupplierUnavailableException.cs`:

```csharp compile=app file=Suppliers/SupplierUnavailableException.cs
namespace Shopfront.Suppliers;

public class SupplierUnavailableException(string message) : Exception(message);
```

`Suppliers/ISupplier.cs`:

```csharp compile=app file=Suppliers/ISupplier.cs
namespace Shopfront.Suppliers;

public interface ISupplier
{
    // Delivers what the supplier can of each SKU: a list of (SKU, units) actually sent.
    Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    );
}
```

`Suppliers/FakeSupplier.cs`:

```csharp compile=app file=Suppliers/FakeSupplier.cs
namespace Shopfront.Suppliers;

// Sends a carton of five of anything asked for, unless it has been taken down.
public class FakeSupplier : ISupplier
{
    public const int Carton = 5;

    public bool IsDown { get; set; }

    public Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    )
    {
        if (IsDown)
            throw new SupplierUnavailableException("The supplier's feed did not answer.");
        return Task.FromResult<IReadOnlyList<(string Sku, int Units)>>(
            [.. skus.Select(sku => (sku, Carton))]
        );
    }
}
```

## The restock

The restock asks the supplier for every product at or below a threshold, and adds what arrives to
the shelves. It asks every time, even when nothing is short, which is how a feed that has gone down
is noticed.

`Trains/Restock/RestockInput.cs`:

```csharp compile=app file=Trains/Restock/RestockInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.Restock;

public record RestockInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
```

`Trains/Restock/Models/Delivery.cs`:

```csharp compile=app file=Trains/Restock/Models/Delivery.cs
namespace Shopfront.Trains.Restock.Models;

public record Delivery(string Sku, int Units);
```

`Trains/Restock/Models/ShortProducts.cs`:

```csharp compile=app file=Trains/Restock/Models/ShortProducts.cs
namespace Shopfront.Trains.Restock.Models;

internal record ShortProducts(List<string> Skus);
```

`Trains/Restock/RestockOutput.cs`:

```csharp compile=app file=Trains/Restock/RestockOutput.cs
using Shopfront.Trains.Restock.Models;

namespace Shopfront.Trains.Restock;

public record RestockOutput(List<Delivery> Delivered);
```

`Trains/Restock/IRestockTrain.cs`:

```csharp compile=app file=Trains/Restock/IRestockTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public interface IRestockTrain : IServiceTrain<RestockInput, RestockOutput>;
```

`Trains/Restock/Junctions/FindShortProductsJunction.cs`:

```csharp compile=app file=Trains/Restock/Junctions/FindShortProductsJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class FindShortProductsJunction(IShopfrontDbContext db)
    : EffectJunction<RestockInput, ShortProducts>
{
    public override async Task<ShortProducts> Run(RestockInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
```

`Trains/Restock/Junctions/ReceiveDeliveryJunction.cs`:

```csharp compile=app file=Trains/Restock/Junctions/ReceiveDeliveryJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Suppliers;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class ReceiveDeliveryJunction(
    ISupplier supplier,
    IShopfrontDbContext db,
    ILogger<ReceiveDeliveryJunction> logger
) : EffectJunction<ShortProducts, RestockOutput>
{
    public override async Task<RestockOutput> Run(ShortProducts shortProducts)
    {
        var delivered = await supplier.Deliver(shortProducts.Skus, CancellationToken);

        foreach (var (sku, units) in delivered)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + units),
                    CancellationToken
                );

        logger.LogInformation("Received {Count} deliveries", delivered.Count);
        return new RestockOutput([.. delivered.Select(d => new Delivery(d.Sku, d.Units))]);
    }
}
```

`Trains/Restock/RestockTrain.cs`:

```csharp compile=app file=Trains/Restock/RestockTrain.cs
using Shopfront.Trains.Restock.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public class RestockTrain : ServiceTrain<RestockInput, RestockOutput>, IRestockTrain
{
    protected override Task<Either<Exception, RestockOutput>> Junctions() =>
        Chain<FindShortProductsJunction>().Chain<ReceiveDeliveryJunction>().Resolve();
}
```

A supplier that does not answer may answer next time, so its failure is transient. The classifier
says so.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Suppliers;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            // Another order changed the stock first; trying again reads the new stock.
            DbUpdateConcurrencyException => FailureClass.Transient,
            // The supplier's feed is down; it comes back.
            SupplierUnavailableException => FailureClass.Transient,
            _ => null,
        };
}
```

The class is recorded and shown, as in chapter 3. It does not decide whether the run is retried:
a manifest's run is retried however it failed, up to its limit.

## Tell staff when scheduled work fails

A lifecycle hook runs on a run's transitions: started, completed, failed, cancelled. One that
reacts to a failure is how staff hear about a scheduled run nobody was watching. It runs for every
train, so it picks out the ones a manifest ran.

`Staff/StaffFailureHook.cs`:

```csharp compile=app file=Staff/StaffFailureHook.cs
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Shopfront.Staff;

// Tells staff about every failed run of scheduled work. A failed order is the customer's to see.
public class StaffFailureHook(IStaffNotifier staff) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        metadata.ManifestId is null
            ? Task.CompletedTask
            : staff.Notify($"{metadata.Name} failed: {exception.Message}", ct);
}
```

A hook's failure is logged and never fails the run it was told about.

The supplier joins the shop's services, once, so the Development switch and the train see the same
one.

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
using Shopfront.Suppliers;
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
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>());
}
```

## Schedule it, with retries

`ManifestNames.cs`:

```csharp compile=app file=ManifestNames.cs
namespace Shopfront;

// The external id of each manifest Shopfront schedules: its identity on every start and every host.
public static class ManifestNames
{
    public const string StockReport = "stock-report";
    public const string LowStockCheck = "low-stock-check";
    public const string Restock = "restock";
}
```

`Program.cs`:

```csharp compile=app file=Program.cs
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

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
        {
            // In Development, retries come seconds apart rather than minutes, to be watched.
            if (development)
                scheduler
                    .DefaultRetryDelay(TimeSpan.FromSeconds(5))
                    .MaxRetryDelay(TimeSpan.FromSeconds(30));

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

    // Takes the fake supplier down, or brings it back.
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

What is new:

- **`MaxRetries(2)`** retries a failed restock twice, so it runs at most three times. The third
  failure makes it a dead letter.
- **The backoff** spaces the retries out. The first waits `DefaultRetryDelay`, five minutes by
  default, and each one after it waits twice as long as the last, up to `MaxRetryDelay`, an hour by
  default. Development uses five seconds and thirty, so the whole story takes less than a minute.
  These are the scheduler's settings, shared by every manifest.
- **`AddLifecycleHook<StaffFailureHook>()`** registers the hook. It goes last in `AddEffects`,
  after the effects that need the data provider before them.
- **The restock runs every hour**, and every minute in Development.
- **`/dev/supplier/{state}`** flips the fake supplier, in Development only.

## Watch it fail

Start the application and open the console as a member of staff, on **Executions**:

```bash
dotnet run --urls http://localhost:5080
```

The first restock runs at once, and the next a minute later. Take the supplier down:

```bash
curl -X POST http://localhost:5080/dev/supplier/down
```

The next restock fails, and the hook tells staff. Then it runs again and fails again, and again
after a longer wait:

```text
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (Shopfront.Suppliers.SupplierUnavailableException) with Message (The supplier's feed did not answer.).
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Shopfront.Trains.Restock.IRestockTrain failed: The supplier's feed did not answer.
```

The runs' start times show the backoff. Each retry runs on the scheduler's next pass after its
delay has gone by, so the gaps are a little longer than five and ten seconds:

```text
 id | train_state | failure_class |          start_time
----+-------------+---------------+-------------------------------
  6 | completed   | unclassified  | 2026-10-18 20:16:57.188407+00
 55 | failed      | transient     | 2026-10-18 20:18:02.674244+00
 65 | failed      | transient     | 2026-10-18 20:18:13.79591+00
 77 | failed      | transient     | 2026-10-18 20:18:27.971728+00
```

The console's **Executions** shows the three failed runs of `IRestockTrain`, each classified
`Transient`. **Dead letters** now holds the restock: the manifest stopped. It will not run again,
on its schedule or otherwise, until someone looks at it.

## Bring it back

```bash
curl -X POST http://localhost:5080/dev/supplier/up
```

The dead letter is still there: fixing the cause does not clear it, because nothing knows the cause
was fixed. In the console, open it under **Dead letters** and choose **Requeue**. The same, as a
member of staff:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { deadLetters { deadLetters(take: 5) { items { id manifestName status reason } } } } }"}'
```

```json
{"data":{"operations":{"deadLetters":{"deadLetters":{"items":[{"id":1,"manifestName":"Shopfront.Trains.Restock.IRestockTrain","status":"AWAITING_INTERVENTION","reason":"Max retries exceeded: (3) failures > (2) max retries"}]}}}}}
```

```bash
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"mutation { operations { deadLetters { requeueDeadLetter(id: 1) { success message } } } }"}'
```

```json
{"data":{"operations":{"deadLetters":{"requeueDeadLetter":{"success":true,"message":"Dead letter requeued"}}}}}
```

The requeue runs at once, with no backoff, and succeeds. The dead letter is resolved, the manifest
is back on its schedule, and its failures no longer count toward the next dead letter.

**Acknowledge** is the other way to resolve one: it records a note and puts the manifest back on
its schedule without running it, for a failure that needs no run to make up for it.

## Test it

The junction is tested against a supplier that is down and one that is not. The hook is tested
on its own.

`tests/Shopfront.Tests/UnitTests/RestockTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/RestockTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Trains.Restock.Junctions;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Models.Metadata;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class RestockTests
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

    private static ReceiveDeliveryJunction Junction(
        FakeSupplier supplier,
        Shopfront.Data.ShopfrontDbContext db
    ) => new(supplier, db, NullLogger<ReceiveDeliveryJunction>.Instance);

    [Test]
    public async Task ReceiveDelivery_SupplierUp_AddsACartonToEachShortProduct()
    {
        await using var db = TestDatabase.NewContext();

        var output = await Junction(new FakeSupplier(), db).Run(new ShortProducts(["CAP", "TEE"]));

        output.Delivered.Should().Equal(new Delivery("CAP", 5), new Delivery("TEE", 5));
        (await TestDatabase.StockOf("CAP")).Should().Be(5);
        (await TestDatabase.StockOf("TEE")).Should().Be(8);
    }

    [Test]
    public async Task ReceiveDelivery_SupplierDown_ThrowsAndChangesNothing()
    {
        await using var db = TestDatabase.NewContext();

        var act = () => Junction(new FakeSupplier { IsDown = true }, db).Run(new ShortProducts(["CAP"]));

        await act.Should().ThrowAsync<SupplierUnavailableException>();
        (await TestDatabase.StockOf("CAP")).Should().Be(0);
    }

    [Test]
    public async Task StaffFailureHook_ScheduledRun_TellsStaff()
    {
        var staff = new RecordingNotifier();
        var metadata = new Metadata { Name = "Shopfront.Trains.Restock.IRestockTrain", ManifestId = 3 };

        await new StaffFailureHook(staff).OnFailed(
            metadata,
            new SupplierUnavailableException("The supplier's feed did not answer."),
            CancellationToken.None
        );

        staff
            .Messages.Should()
            .Equal(
                "Shopfront.Trains.Restock.IRestockTrain failed: The supplier's feed did not answer."
            );
    }

    [Test]
    public async Task StaffFailureHook_RunOnARequest_SaysNothing()
    {
        var staff = new RecordingNotifier();

        await new StaffFailureHook(staff).OnFailed(
            new Metadata { Name = "Shopfront.Trains.PlaceOrder.IPlaceOrderTrain" },
            new InvalidOperationException(),
            CancellationToken.None
        );

        staff.Messages.Should().BeEmpty();
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 58 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront restocks from a supplier that goes down, and copes: each failure is recorded and
classified, the restock is tried twice more with a growing delay, and after the third failure it
stops and waits for a person, who is told and who sends it round again from the console once the
supplier is back.

Everything still runs in one process, the one customers are talking to. The next chapter moves the
work nobody waits for onto a process of its own.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
