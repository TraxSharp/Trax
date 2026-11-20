using Shopfront.Data.Models;
using Shopfront.Trains.ReturnOrder;
using Shopfront.Trains.ReturnOrder.Junctions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class CheckTheOrderJunctionTests
{
    private static readonly Guid AdasOrder = Guid.NewGuid();

    [SetUp]
    public async Task PlaceAdasOrder()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        db.Orders.Add(
            new Order
            {
                Id = AdasOrder,
                CustomerId = "TraxJwt:ada",
                Total = 24.00m,
                TransactionId = "tx_test",
                PlacedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<bool> Check(string orderId, string customerId)
    {
        var db = TestDatabase.NewContext();
        await using (db.ConfigureAwait(false))
        {
            var output = await new CheckTheOrderJunction(db)
                .Run(new ReturnOrderInput { OrderId = orderId, CustomerId = customerId })
                .ConfigureAwait(false);
            return output.Accepted;
        }
    }

    [Test]
    public async Task Run_HerOwnOrder_IsAccepted() =>
        (await Check(AdasOrder.ToString(), "TraxJwt:ada")).Should().BeTrue();

    [Test]
    public async Task Run_SomeoneElsesOrder_IsRefused() =>
        (await Check(AdasOrder.ToString(), "TraxJwt:bo")).Should().BeFalse();

    [Test]
    public async Task Run_NotAnOrderId_IsRefused() =>
        (await Check("not-an-order", "TraxJwt:ada")).Should().BeFalse();
}
