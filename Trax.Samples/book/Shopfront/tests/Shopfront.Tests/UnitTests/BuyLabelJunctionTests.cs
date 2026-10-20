using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Couriers;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Warehouse;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class BuyLabelJunctionTests
{
    private static readonly FulfilOrderInput Order = new()
    {
        OrderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89"),
        Items = 2,
    };

    [Test]
    public async Task Run_PricesTheLabelByTheBoxsWeight()
    {
        var junction = new BuyLabelJunction(
            new FakeCourier(),
            NullLogger<BuyLabelJunction>.Instance
        );

        var label = await junction.Run(
            (Order, new Box("Small", 1m), new CourierRates(4.00m, 1.50m))
        );

        label.Price.Should().Be(5.50m);
        label.TrackingNumber.Should().Be("TRK-3179F9C4");
    }

    [Test]
    public async Task Run_Twice_BuysOneLabel()
    {
        var courier = new FakeCourier();
        var junction = new BuyLabelJunction(courier, NullLogger<BuyLabelJunction>.Instance);
        var rates = new CourierRates(4.00m, 1.50m);

        var first = await junction.Run((Order, new Box("Small", 1m), rates));
        var again = await junction.Run((Order, new Box("Large", 3m), rates));

        again
            .TrackingNumber.Should()
            .Be(first.TrackingNumber, "the courier keys a label by its order");
    }
}
