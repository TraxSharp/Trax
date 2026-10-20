using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class FindShortProductsJunction(IShopfrontDbContext db)
    : EffectJunction<RestockInput, ShortProducts>
{
    public override async Task<ShortProducts> Run(RestockInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
