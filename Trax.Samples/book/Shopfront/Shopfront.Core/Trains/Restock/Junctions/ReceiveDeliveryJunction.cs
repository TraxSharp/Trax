using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Suppliers;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class ReceiveDeliveryJunction(
    ISupplier supplier,
    IShopfrontDbContext db,
    ILogger<ReceiveDeliveryJunction> logger
) : EffectJunction<ShortProducts, RestockOutput>
{
    public override async Task<RestockOutput> Run(ShortProducts shortProducts)
    {
        var delivered = await supplier.Deliver(shortProducts.Skus, CancellationToken);

        foreach (var (sku, units) in delivered)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + units),
                    CancellationToken
                );

        logger.LogInformation("Received {Count} deliveries", delivered.Count);
        return new RestockOutput([.. delivered.Select(d => new Delivery(d.Sku, d.Units))]);
    }
}
