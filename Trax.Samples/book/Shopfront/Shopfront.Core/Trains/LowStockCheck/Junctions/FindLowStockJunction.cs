using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class FindLowStockJunction(IShopfrontDbContext db)
    : EffectJunction<LowStockCheckInput, LowStockCheckOutput>
{
    public override async Task<LowStockCheckOutput> Run(LowStockCheckInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
