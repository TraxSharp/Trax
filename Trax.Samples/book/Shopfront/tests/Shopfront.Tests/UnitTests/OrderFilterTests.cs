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
