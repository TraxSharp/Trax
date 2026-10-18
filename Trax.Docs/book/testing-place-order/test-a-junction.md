---
title: "2.3 Test a junction"
description: "Chapter 2 of the Trax book, section 3: Test a junction."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/testing-place-order/add-a-solution-and-a-test-project
---

# 2.3 Test a junction

A junction is a class with a `Run` method, so it is tested by constructing it and calling `Run`.
There is no container, no database and no host.

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
        new() { CustomerId = "ada", Lines = [.. lines] };

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

`NullLogger` stands in for the logger, which the test does not read. The catalogue is the real one,
because it is a plain class with known contents: a fake would only repeat them.

`tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ReserveStockJunctionTests
{
    private static PricedCart Cart(params PricedLine[] lines) =>
        new([.. lines], lines.Sum(l => l.UnitPrice * l.Quantity));

    [Test]
    public async Task Run_InStock_ReservesEveryLine()
    {
        var catalogue = new Catalogue();
        var junction = new ReserveStockJunction(
            catalogue,
            NullLogger<ReserveStockJunction>.Instance
        );

        await junction.Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        catalogue.TryReserve([("TEE", 1)]).Should().Be("TEE", "all three T-shirts are reserved");
    }

    [Test]
    public async Task Run_OneLineOutOfStock_ThrowsAndReservesNothing()
    {
        var catalogue = new Catalogue();
        var junction = new ReserveStockJunction(
            catalogue,
            NullLogger<ReserveStockJunction>.Instance
        );

        var act = () => junction.Run(Cart(new PricedLine("MUG", 1, 12.00m), new PricedLine("CAP", 1, 18.50m)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        catalogue.TryReserve([("MUG", 10)]).Should().BeNull("the mug was not reserved either");
    }
}
```

The second test checks the promise chapter 1 made: when one line is short, nothing is reserved.
It asks the catalogue for all ten mugs afterwards, which only succeeds if none were taken.

The payment provider is an interface, and the test needs to know what it was asked to do, so here a
fake earns its place. It records each call and answers with a fixed transaction id.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class RecordingPaymentProvider : IPaymentProvider
    {
        public List<(string CustomerId, decimal Amount)> Charges { get; } = [];

        public Task<string> Charge(
            string customerId,
            decimal amount,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, amount));
            return Task.FromResult("tx_test");
        }
    }

    [Test]
    public async Task Run_PricedCart_ChargesTheCustomerTheTotal()
    {
        var payments = new RecordingPaymentProvider();
        var junction = new TakePaymentJunction(payments, NullLogger<TakePaymentJunction>.Instance);
        var order = new PlaceOrderInput { CustomerId = "ada", Lines = [new("MUG", 2)] };
        var cart = new PricedCart([new("MUG", 2, 12.00m)], 24.00m);

        var payment = await junction.Run((order, cart));

        payments.Charges.Should().Equal(("ada", 24.00m));
        payment.Should().Be(new Payment("tx_test", 24.00m));
    }
}
```

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.4 Test the train](/docs/book/testing-place-order/test-the-train)
