---
title: "3.6 Test it"
description: "Chapter 3 of the Trax book, section 6: Test it."
type: tutorial
area: general
parent: "3. Every order is recorded"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/every-order-is-recorded/read-the-runs-back
---

# 3.6 Test it

The tests follow the code. The train's input has a payment token now, so the tests that build one
give it one.

`tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PriceCartJunctionTests
{
    private static PriceCartJunction Junction() =>
        new(new Catalogue(), NullLogger<PriceCartJunction>.Instance);

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

The payment junction's tests check the new promise: a failed charge puts the stock back. The fake
here can be told to fail.

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

    // A catalogue whose three T-shirts are reserved, as ReserveStockJunction leaves it.
    private static Catalogue ReservedCatalogue()
    {
        var catalogue = new Catalogue();
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull();
        return catalogue;
    }

    private static TakePaymentJunction Junction(IPaymentProvider payments, Catalogue catalogue) =>
        new(payments, catalogue, NullLogger<TakePaymentJunction>.Instance);

    [Test]
    public async Task Run_Charged_ReturnsThePaymentWithItsReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Junction(payments, ReservedCatalogue())
            .Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        payments.Charges.Should().Equal(("ada", "tok_visa", 75.00m));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var catalogue = ReservedCatalogue();
        var payments = new ScriptedPaymentProvider(
            new PaymentDeclinedException("The card was declined.")
        );

        var act = () =>
            Junction(payments, catalogue).Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull("the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var catalogue = ReservedCatalogue();
        var payments = new ScriptedPaymentProvider(new TimeoutException("No answer."));

        var act = () =>
            Junction(payments, catalogue).Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull("the three T-shirts are back");
    }
}
```

The classifier is a function from an exception to a class, so its test is a table.

`tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs
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
        yield return new TestCaseData(new PaymentDeclinedException("Declined."), null);
        yield return new TestCaseData(new InvalidOperationException(), null);
    }

    [TestCaseSource(nameof(Cases))]
    public void Classify_Exception_GivesItsClass(Exception exception, FailureClass? expected) =>
        new ShopfrontFailureClassifier().Classify(exception).Should().Be(expected);
}
```

A `PaymentDeclinedException` never reaches the classifier, because the junction turns it into a
`TrainException` first; the test pins that the classifier has no opinion of its own about it.

The train test gains the classifier and a test for each way the payment fails.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
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
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Catalogue>();
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
    public async Task RunAsync_InStock_ConfirmsTheOrderAndMasksTheReceipt()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        output.ReceiptUrl.Should().StartWith("https://pay.example/receipts/");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
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
    public async Task RunAsync_Declined_FailsAtTakePaymentAsPermanent()
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

The host tests now start an application that records into Postgres, so they need the database
from the top of this chapter running. They send a payment token as well.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public void StartTheApplication() => _app = new WebApplicationFactory<Program>();

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

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 1 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Every run of `PlaceOrder` is a row in Postgres with its input, its output and, when it fails, the
junction that failed, the reason and whether trying again could help. A failed payment gives its
stock back, and the receipt link never reaches the record.

The stock itself is still a dictionary in memory, and the order is confirmed without being stored
anywhere but in Trax's record of the run. The next chapter gives Shopfront a database of its own:
products, their stock and the orders placed against them.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [4. The shop's own data](/docs/book/the-shops-own-data)
