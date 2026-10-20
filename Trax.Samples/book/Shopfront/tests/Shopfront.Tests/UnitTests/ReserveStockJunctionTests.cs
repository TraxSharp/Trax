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
