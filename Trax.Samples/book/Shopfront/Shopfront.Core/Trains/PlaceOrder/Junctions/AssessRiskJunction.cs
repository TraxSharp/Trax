using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class AssessRiskJunction(IShopfrontDbContext db)
    : EffectJunction<(Customer Customer, PricedCart Cart), OrderRisk>
{
    public override async Task<OrderRisk> Run((Customer Customer, PricedCart Cart) input)
    {
        var ordered = await db.Orders.AnyAsync(
            o => o.CustomerId == input.Customer.Id,
            CancellationToken
        );
        return new OrderRisk(input.Customer.Id, input.Cart.Total, FirstOrder: !ordered);
    }
}
