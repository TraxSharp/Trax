using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.StockReport.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.StockReport.Junctions;

internal class CountStockJunction(IShopfrontDbContext db, ILogger<CountStockJunction> logger)
    : EffectJunction<StockReportInput, StockReportOutput>
{
    public override async Task<StockReportOutput> Run(StockReportInput input)
    {
        var lines = await db
            .Products.OrderBy(p => p.Sku)
            .Select(p => new StockLine(p.Sku, p.Stock))
            .ToListAsync(CancellationToken);

        var report = new StockReportOutput(DateTime.UtcNow, lines.Sum(l => l.Stock), lines);
        logger.LogInformation(
            "Counted {Units} units across {Products} products",
            report.Units,
            lines.Count
        );
        return report;
    }
}
