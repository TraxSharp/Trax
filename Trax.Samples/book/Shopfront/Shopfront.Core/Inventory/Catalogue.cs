using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Inventory;

// What the shop sells and how many of each it has.
public class Catalogue(IShopfrontDbContext db)
{
    // The price of each of these SKUs that the shop sells.
    public Task<Dictionary<string, decimal>> PricesOf(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken
    ) =>
        db.Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, p => p.Price, cancellationToken);

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public async Task<string?> TryReserve(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        var skus = lines.Select(l => l.Sku).ToList();
        var products = await db
            .Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, cancellationToken);

        var shortSku = lines
            .Where(l => products[l.Sku].Stock < l.Quantity)
            .Select(l => l.Sku)
            .FirstOrDefault();
        if (shortSku is not null)
            return shortSku;

        foreach (var (sku, quantity) in lines)
            products[sku].Stock -= quantity;

        // One transaction for every line. If another order changed one of these products since
        // it was read, nothing is saved and DbUpdateConcurrencyException says so.
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // Puts reserved stock back. Each line is one atomic increment, so it never overwrites
    // another order's change.
    public async Task Release(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        foreach (var (sku, quantity) in lines)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + quantity),
                    cancellationToken
                );
    }
}
