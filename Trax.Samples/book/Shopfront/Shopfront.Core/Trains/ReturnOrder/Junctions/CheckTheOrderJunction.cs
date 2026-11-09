using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.ReturnOrder.Junctions;

// Accepts a return only of an order the return's owner placed. An id that names no order of
// theirs, or is not an id at all, is refused rather than failed: the customer can fix it.
internal class CheckTheOrderJunction(IShopfrontDbContext db)
    : EffectJunction<ReturnOrderInput, ReturnOrderOutput>
{
    public override async Task<ReturnOrderOutput> Run(ReturnOrderInput input)
    {
        if (!Guid.TryParse(input.OrderId, out var orderId))
            return new ReturnOrderOutput(Accepted: false);

        var theirs = await db.Orders.AnyAsync(
            o => o.Id == orderId && o.CustomerId == input.CustomerId,
            CancellationToken
        );
        return new ReturnOrderOutput(theirs);
    }
}
