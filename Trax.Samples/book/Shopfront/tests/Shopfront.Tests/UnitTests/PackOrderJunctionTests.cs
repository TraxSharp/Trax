using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PackOrderJunctionTests
{
    [Test]
    public async Task Run_PacksTheOrderUnderItsLabel()
    {
        var orderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89");
        var junction = new PackOrderJunction(
            new FakeWarehouse { PackingTime = TimeSpan.Zero },
            NullLogger<PackOrderJunction>.Instance
        );

        var parcel = await junction.Run(
            (
                new FulfilOrderInput { OrderId = orderId, Items = 2 },
                new ShippingLabel("TRK-1", 5.50m)
            )
        );

        parcel.Should().Be(new FulfilOrderOutput(orderId, "TRK-1"));
    }
}
