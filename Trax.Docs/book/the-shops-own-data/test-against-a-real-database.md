---
title: "4.9 Test against a real database"
description: "Chapter 4 of the Trax book, section 9: Test against a real database."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/the-shops-own-data/run-it
---

# 4.9 Test against a real database

The junction tests in chapter 2 needed no database. Now pricing and reserving are queries, and a
test that replaces the database with a fake would test the fake. They run against a database of
their own, `shopfront_tests`, on the same Postgres, which each test drops and creates again, so it
starts from the seeded catalogue with no orders.

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

    public static ShopfrontDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ShopfrontDbContext>().UseNpgsql(ConnectionString).Options);

    // Drops and creates the database: the seeded catalogue, and no orders or runs.
    public static async Task Reset()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public static async Task<int> StockOf(string sku)
    {
        await using var db = NewContext();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Stock).SingleAsync();
    }
}
```

`tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Data;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PriceCartJunctionTests
{
    private ShopfrontDbContext _db = null!;

    [SetUp]
    public async Task ResetTheDatabase()
    {
        await TestDatabase.Reset();
        _db = TestDatabase.NewContext();
    }

    [TearDown]
    public ValueTask DisposeTheContext() => _db.DisposeAsync();

    private PriceCartJunction Junction() =>
        new(new Catalogue(_db), NullLogger<PriceCartJunction>.Instance);

    private static PlaceOrderInput Order(params OrderLine[] lines) =>
        new()
        {
            CustomerId = "ada",
            Lines = [.. lines],
            PaymentToken = "tok_visa",
        };

    [Test]
    public async Task Run_TwoLines_PricesEachAndTotalsThem()
    {
        var cart = await Junction().Run(Order(new OrderLine("MUG", 2), new OrderLine("TEE", 1)));

        cart.Lines.Select(l => l.UnitPrice).Should().Equal(12.00m, 25.00m);
        cart.Total.Should().Be(49.00m);
    }

    [Test]
    public async Task Run_EmptyCart_Throws()
    {
        var act = () => Junction().Run(Order());

        await act.Should().ThrowAsync<TrainException>().WithMessage("The cart is empty.");
    }

    [Test]
    public async Task Run_UnknownSku_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("HAT", 1)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("HAT is not in the catalogue.");
    }

    [Test]
    public async Task Run_ZeroQuantity_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("MUG", 0)));

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("MUG: the quantity must be at least 1.");
    }
}
```

The stock tests read the stock back through a context of their own, which sees what was committed
rather than what one context remembers.

`tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Data;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ReserveStockJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private static ReserveStockJunction Junction(ShopfrontDbContext db) =>
        new(new Catalogue(db), NullLogger<ReserveStockJunction>.Instance);

    private static PricedCart Cart(params PricedLine[] lines) =>
        new([.. lines], lines.Sum(l => l.UnitPrice * l.Quantity));

    [Test]
    public async Task Run_InStock_ReservesEveryLine()
    {
        await using var db = TestDatabase.NewContext();

        await Junction(db).Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_OneLineOutOfStock_ThrowsAndReservesNothing()
    {
        await using var db = TestDatabase.NewContext();

        var act = () =>
            Junction(db)
                .Run(Cart(new PricedLine("MUG", 1, 12.00m), new PricedLine("CAP", 1, 18.50m)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        (await TestDatabase.StockOf("MUG")).Should().Be(10, "the mug was not reserved either");
    }

    [Test]
    public async Task Run_StockChangedSinceItWasRead_IsRefused()
    {
        await using var first = TestDatabase.NewContext();
        await using var second = TestDatabase.NewContext();
        // Both orders read the three T-shirts before either saves.
        await first.Products.SingleAsync(p => p.Sku == "TEE");
        await second.Products.SingleAsync(p => p.Sku == "TEE");

        await Junction(first).Run(Cart(new PricedLine("TEE", 3, 25.00m)));
        var act = () => Junction(second).Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(0, "the T-shirts were sold once");
    }
}
```

The third test is the race from the top of the chapter, played out in order: two contexts read the
same row, the first saves, and the second is refused.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class ScriptedPaymentProvider(Exception? failWith = null) : IPaymentProvider
    {
        public List<(string CustomerId, string PaymentToken, decimal Amount)> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount));
            return failWith is null
                ? Task.FromResult(new ChargeResult("tx_test", "https://pay.example/receipts/tx_test"))
                : Task.FromException<ChargeResult>(failWith);
        }
    }

    private static readonly PlaceOrderInput Order = new()
    {
        CustomerId = "ada",
        Lines = [new OrderLine("TEE", 3)],
        PaymentToken = "tok_visa",
    };

    private static readonly PricedCart Cart = new([new PricedLine("TEE", 3, 25.00m)], 75.00m);

    // Resets the database and reserves the three T-shirts, as ReserveStockJunction leaves them.
    [SetUp]
    public async Task ReserveTheTShirts()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        (await new Catalogue(db).TryReserve([("TEE", 3)], CancellationToken.None))
            .Should()
            .BeNull();
    }

    private static async Task<Payment> Run(IPaymentProvider payments)
    {
        await using var db = TestDatabase.NewContext();
        var junction = new TakePaymentJunction(
            payments,
            new Catalogue(db),
            NullLogger<TakePaymentJunction>.Instance
        );
        return await junction.Run((Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_Charged_ReturnsThePaymentWithItsReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("ada", "tok_visa", 75.00m));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var act = () =>
            Run(new ScriptedPaymentProvider(new PaymentDeclinedException("The card was declined.")));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var act = () => Run(new ScriptedPaymentProvider(new TimeoutException("No answer.")));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }
}
```

`tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ConfirmOrderJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    [Test]
    public async Task Run_Paid_StoresTheOrderWithItsPrices()
    {
        var input = new PlaceOrderInput
        {
            CustomerId = "ada",
            Lines = [new OrderLine("MUG", 2)],
            PaymentToken = "tok_visa",
        };
        var cart = new PricedCart([new PricedLine("MUG", 2, 12.00m)], 24.00m);
        var payment = new Payment("tx_test", 24.00m, "https://pay.example/receipts/tx_test");

        await using (var db = TestDatabase.NewContext())
        {
            var output = await new ConfirmOrderJunction(
                db,
                NullLogger<ConfirmOrderJunction>.Instance
            ).Run((input, cart, payment));

            output.ReceiptUrl.Should().Be(payment.ReceiptUrl);
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("ada");
        order.TransactionId.Should().Be("tx_test");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

`tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Failures;
using Shopfront.Payments;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ShopfrontFailureClassifierTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(new TrainException("CAP is out of stock."), FailureClass.Permanent);
        yield return new TestCaseData(new TimeoutException(), FailureClass.Transient);
        yield return new TestCaseData(new DbUpdateConcurrencyException(), FailureClass.Transient);
        yield return new TestCaseData(new PaymentDeclinedException("Declined."), null);
        yield return new TestCaseData(new InvalidOperationException(), null);
    }

    [TestCaseSource(nameof(Cases))]
    public void Classify_Exception_GivesItsClass(Exception exception, FailureClass? expected) =>
        new ShopfrontFailureClassifier().Classify(exception).Should().Be(expected);
}
```

The train test registers the context against the test database and checks that a confirmed order
is stored.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task BuildTheContainer()
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainDataContext<IShopfrontDbContext, ShopfrontDbContext>(options =>
            options.UseNpgsql(TestDatabase.ConnectionString)
        );
        services.AddScoped<Catalogue>();
        services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    private static PlaceOrderInput Order(string customerId, string paymentToken, string sku) =>
        new()
        {
            CustomerId = customerId,
            Lines = [new OrderLine(sku, 1)],
            PaymentToken = paymentToken,
        };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderAndMasksTheReceipt()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).Id.Should().Be(output.OrderId);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(
                Order(customerId, FakePaymentProvider.DeclinedToken, "MUG")
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(Guid.NewGuid().ToString(), "tok_visa", "MUG"));
        var customerId = Guid.NewGuid().ToString();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
    }

    private Metadata RunFor(string customerId)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(customerId) == true);
    }
}
```

The host tests point the application at the test database by overriding its connection string.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new
        {
            customerId = "ada",
            paymentToken,
            lines = new[] { new { sku, quantity = 1 } },
        };

    [Test]
    public async Task PostOrders_InStock_Answers200WithTheOrder()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        order!.Total.Should().Be(12.00m);
    }

    [Test]
    public async Task PostOrders_OutOfStock_Answers422WithTheReason()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("CAP"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("CAP is out of stock.");
    }

    [Test]
    public async Task PostOrders_Declined_Answers422WithTheReason()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync("/orders", Order("MUG", "tok_chargeDeclined"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("The card was declined.");
    }
}
```

The data-layer guards hold the context to the rules this chapter followed: it derives
`DomainDataContext`, it has its companion interface in a file of its own, and its schema is its
alone.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Shopfront.Data;
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
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["Data"] };

    protected override IReadOnlyList<Type> DomainContexts => [typeof(ShopfrontDbContext)];
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34, Duration: 15 s - Shopfront.Tests.dll (net10.0)
```

Dropping and creating the database for each test is what makes these slower than the ones in
chapter 2, and what makes each one start from the same shelves.

## What we have

Shopfront has its own schema beside Trax's. Products, their stock and every confirmed order live in
it, a lost race for the last item is refused rather than sold twice, and the tests run against a
real database of their own.

Every order that is in stock and paid for goes through. The next chapter stops one that looks like
fraud before it is charged, with a rule the train asks rather than an `if` inside a junction.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [5. Does this order look like fraud](/docs/book/does-this-order-look-like-fraud)
