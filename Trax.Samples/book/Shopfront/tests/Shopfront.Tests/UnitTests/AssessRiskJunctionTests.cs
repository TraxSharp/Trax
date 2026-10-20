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
