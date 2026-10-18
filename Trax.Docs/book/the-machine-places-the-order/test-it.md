---
title: "13.7 Test it"
description: "Chapter 13 of the Trax book, section 7: Test it."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/the-machine-places-the-order/when-the-order-fails
---

# 13.7 Test it

The payment junction's tests gain the key, and one more: a charge with a key it has seen is not
made again.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.DependencyInjection;
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
        public List<(
            string CustomerId,
            string PaymentToken,
            decimal Amount,
            string? Key
        )> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            string? idempotencyKey,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount, idempotencyKey));
            return failWith is null
                ? Task.FromResult(
                    new ChargeResult("tx_test", "https://pay.example/receipts/tx_test")
                )
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
        await using var services = new ServiceCollection().BuildServiceProvider();
        var junction = new TakePaymentJunction(
            payments,
            new Catalogue(db),
            services,
            NullLogger<TakePaymentJunction>.Instance
        );
        return await junction.Run((Ada, Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_NotInvokedByACheckout_ChargesWithoutAKey()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("TraxJwt:ada", "tok_visa", 75.00m, (string?)null));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var act = () =>
            Run(
                new ScriptedPaymentProvider(new PaymentDeclinedException("The card was declined."))
            );

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

    [Test]
    public async Task FakeProvider_SameKey_AnswersTheFirstChargeAgain()
    {
        var provider = new FakePaymentProvider();

        var first = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );
        var again = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );

        again.Should().Be(first, "a retry with the same key is not charged again");
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

            output.TransactionId.Should().Be("tx_test");
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("TraxJwt:ada");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

The train test no longer looks for the receipt link in the output, which no longer has one.

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
        run.Output.Should().Contain(output.TransactionId);
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

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    71, Skipped:     0, Total:    71 - Shopfront.Tests.dll (net10.0)
```

## What we have

Checkout places the order itself: entering `Placing` runs `PlaceOrder` on the worker, as the
checkout's owner, and the run's end moves the checkout, to `Placed` with the order's id or to
`Failed`, from where the customer retries without being charged twice. A placed order's parcel is
a machine of its own that the checkout waits on.

That ends the part on machines. The next part goes back to fulfilment, to run its checks side by
side and to pick it up where it stopped.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [14. Checks in parallel, and resuming](/docs/book/checks-in-parallel-and-resuming)
