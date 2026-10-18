---
title: "6.9 Test it"
description: "Chapter 6 of the Trax book, section 9: Test it."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/customers-and-staff/what-it-refuses
---

# 6.9 Test it

The tests run `PlaceOrder` through the train bus, which is trusted and checks no attribute, so the
train tests give the run a caller themselves. The host tests sign in for real, with the development
key, and check what each caller can do.

Tests read the database as a member of staff unless they say otherwise, so they see every order.

`tests/Shopfront.Tests/TestCaller.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/TestCaller.cs
using Shopfront.Data;

namespace Shopfront.Tests;

internal sealed record TestCaller(string? CustomerId, bool IsStaff) : IShopfrontCaller
{
    public static readonly TestCaller Staff = new(null, IsStaff: true);

    public static TestCaller Customer(string id) => new(id, IsStaff: false);
}
```

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
            new DbContextOptionsBuilder<ShopfrontDbContext>().UseNpgsql(ConnectionString).Options,
            caller ?? TestCaller.Staff
        );

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

The junction tests change only where their inputs did.

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
        new() { Lines = [.. lines], PaymentToken = "tok_visa" };

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

`tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs
using Shopfront.Data.Models;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class AssessRiskJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private static readonly PricedCart Cart = new([new PricedLine("MUG", 1, 12.00m)], 12.00m);

    [Test]
    public async Task Run_NoEarlierOrder_IsAFirstOrder()
    {
        await using var db = TestDatabase.NewContext();

        var risk = await new AssessRiskJunction(db).Run((new Customer("TraxJwt:ada"), Cart));

        risk.Should().Be(new OrderRisk("TraxJwt:ada", 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task Run_AnEarlierOrder_IsNotAFirstOrder()
    {
        await using (var seed = TestDatabase.NewContext())
        {
            seed.Orders.Add(
                new Order
                {
                    CustomerId = "TraxJwt:ada",
                    TransactionId = "tx_earlier",
                    Total = 12.00m,
                }
            );
            await seed.SaveChangesAsync();
        }
        // Read as ada herself, through the filter, as a run of her order does.
        await using var db = TestDatabase.NewContext(TestCaller.Customer("TraxJwt:ada"));

        var risk = await new AssessRiskJunction(db).Run((new Customer("TraxJwt:ada"), Cart));

        risk.FirstOrder.Should().BeFalse();
    }
}
```

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

    private static readonly Customer Ada = new("TraxJwt:ada");

    private static readonly PlaceOrderInput Order = new()
    {
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
        return await junction.Run((Ada, Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_Charged_ChargesTheCallerAndReturnsTheReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("TraxJwt:ada", "tok_visa", 75.00m));
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
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ConfirmOrderJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    [Test]
    public async Task Run_Paid_StoresTheOrderForTheCaller()
    {
        var cart = new PricedCart([new PricedLine("MUG", 2, 12.00m)], 24.00m);
        var payment = new Payment("tx_test", 24.00m, "https://pay.example/receipts/tx_test");

        await using (var db = TestDatabase.NewContext())
        {
            var output = await new ConfirmOrderJunction(
                db,
                NullLogger<ConfirmOrderJunction>.Instance
            ).Run((new Customer("TraxJwt:ada"), cart, payment));

            output.ReceiptUrl.Should().Be(payment.ReceiptUrl);
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("TraxJwt:ada");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

The filter gets tests of its own: the census below proves it exists, these prove it compares the
right column.

`tests/Shopfront.Tests/UnitTests/OrderFilterTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/OrderFilterTests.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Data.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class OrderFilterTests
{
    [SetUp]
    public async Task OneOrderEach()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        foreach (var customer in new[] { "TraxJwt:ada", "TraxJwt:grace" })
            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customer,
                    TransactionId = $"tx_{customer}",
                    Total = 12.00m,
                    Items =
                    [
                        new OrderItem
                        {
                            Sku = "MUG",
                            Quantity = 1,
                            UnitPrice = 12.00m,
                        },
                    ],
                }
            );
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> OwnersSeenBy(IShopfrontCaller caller)
    {
        await using var db = TestDatabase.NewContext(caller);
        return await db.Orders.Select(o => o.CustomerId).OrderBy(c => c).ToListAsync();
    }

    [Test]
    public async Task Orders_ACustomer_SeesOnlyTheirOwn() =>
        (await OwnersSeenBy(TestCaller.Customer("TraxJwt:ada"))).Should().Equal("TraxJwt:ada");

    [Test]
    public async Task Orders_Staff_SeeEveryOrder() =>
        (await OwnersSeenBy(TestCaller.Staff)).Should().Equal("TraxJwt:ada", "TraxJwt:grace");

    [Test]
    public async Task Orders_Nobody_SeesNothing() =>
        (await OwnersSeenBy(NoShopfrontCaller.Instance)).Should().BeEmpty();

    [Test]
    public async Task Items_ACustomer_SeesOnlyTheirOrdersItems()
    {
        await using var db = TestDatabase.NewContext(TestCaller.Customer("TraxJwt:ada"));

        (await db.Set<OrderItem>().CountAsync()).Should().Be(1);
    }
}
```

The train tests give each run a signed-in customer, and find its record by a payment token no
other test uses, now that the input has no customer in it.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Fraud;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private const string Ada = "TraxJwt:ada";

    private ServiceProvider _provider = null!;

    private async Task Build(Action<IServiceCollection>? replace = null)
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        // The caller a signed-in request would have given the run.
        services.AddScoped(_ => new TraxPrincipal(Ada, "ada", [Roles.Customer]));
        services.AddScoped<IShopfrontCaller>(_ => TestCaller.Customer(Ada));
        replace?.Invoke(services);
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    // A payment token no other test uses, to find this run's record among any others.
    private static string NewToken() => $"tok_{Guid.NewGuid():N}";

    private static PlaceOrderInput Order(string paymentToken, string sku, int quantity = 1) =>
        new() { Lines = [new OrderLine(sku, quantity)], PaymentToken = paymentToken };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderForTheCaller()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).CustomerId.Should().Be(Ada);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_LargeFirstOrder_IsHeldBeforeStockOrPayment()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG", 9));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*You have not been charged.");
        RunFor(token).FailureJunction.Should().Be("RefuseOrderJunction");
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_DeciderSaysYes_HoldsEvenASmallOrder()
    {
        var decider = new ScriptedDecider().YesNo<LooksFraudulent>(0.9);
        await Build(services => services.AddSingleton<IDecider>(decider));
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));

        await act.Should().ThrowAsync<TrainException>();
        decider
            .Requests.Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(new OrderRisk(Ada, 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(Order(FakePaymentProvider.DeclinedToken, "MUG"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(FakePaymentProvider.DeclinedToken);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));
        var token = NewToken();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(token);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
    }

    private Metadata RunFor(string paymentToken)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(paymentToken) == true);
    }
}
```

The host tests are where `[TraxAuthorize]` and the endpoints are checked, because that is where a
real caller signs in. They mint tokens with the development key, which the host accepts in
Development, the environment `WebApplicationFactory` starts it in here.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
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
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private HttpClient SignedIn(string user, string role)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, role)
        );
        return client;
    }

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new { paymentToken, lines = new[] { new { sku, quantity = 1 } } };

    private sealed record OrderRow(Guid Id, string CustomerId, decimal Total);

    [Test]
    public async Task PostOrders_NotSignedIn_Answers401()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostOrders_Staff_Answers403()
    {
        var response = await SignedIn("lin", Roles.Staff).PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PostOrders_Customer_PlacesTheOrderAsThemselves()
    {
        var ada = SignedIn("ada", Roles.Customer);

        var response = await ada.PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        var mine = await ada.GetFromJsonAsync<List<OrderRow>>("/orders");
        mine.Should().ContainSingle().Which.Should().Be(new OrderRow(order!.OrderId, "TraxJwt:ada", 12.00m));
    }

    [Test]
    public async Task PostOrders_Declined_Answers422WithTheReason()
    {
        var response = await SignedIn("ada", Roles.Customer)
            .PostAsJsonAsync("/orders", Order("MUG", "tok_chargeDeclined"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("The card was declined.");
    }

    [Test]
    public async Task GetOrders_EachCustomerSeesTheirOwn_StaffSeeAll()
    {
        var ada = SignedIn("ada", Roles.Customer);
        var grace = SignedIn("grace", Roles.Customer);
        await ada.PostAsJsonAsync("/orders", Order("MUG"));
        await grace.PostAsJsonAsync("/orders", Order("MUG"));

        (await grace.GetFromJsonAsync<List<OrderRow>>("/orders"))!
            .Select(o => o.CustomerId)
            .Should()
            .Equal("TraxJwt:grace");
        (await SignedIn("lin", Roles.Staff).GetFromJsonAsync<List<OrderRow>>("/orders"))!
            .Select(o => o.CustomerId)
            .Should()
            .BeEquivalentTo(["TraxJwt:ada", "TraxJwt:grace"]);
    }
}
```

The last test is the one the owner-scope guard asks every application to write: one customer
creates a row, a second customer reads, and gets nothing of the first's.

The data-layer guard learns which rows belong to someone. Its owner-scope census reads the model
and fails on any entity that holds an owner's id, or hangs off one that does, without a filter that
reads the caller.

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
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["Data"] };

    protected override IReadOnlyList<Type> DomainContexts => [typeof(ShopfrontDbContext)];

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

`OwnerType` is the entity rows belong to. Shopfront has no customer table, so it is the order
itself, owned through `CustomerId`, and the census then also requires a filter on `OrderItem`,
which hangs off it. Delete the item filter and this guard fails, naming `OrderItem`.

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49, Duration: 19 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront knows who is asking. A customer orders as themselves and reads only their own orders, a
member of staff reads every order and places none, and a caller nobody signed in is refused at the
door. The rules live where they hold everywhere: who may run a train on the train, which rows a
caller may read on the data. A host missing either check refuses to start.

The API is two hand-written endpoints. The next chapter replaces them with GraphQL: the catalogue
for everyone, `PlaceOrder` for customers, and each customer's orders, all from the same rules.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [7. Serve it over GraphQL](/docs/book/serve-it-over-graphql)
