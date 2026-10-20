using Shopfront.Trains.StockReport.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public class StockReportTrain : ServiceTrain<StockReportInput, StockReportOutput>, IStockReportTrain
{
    protected override Task<Either<Exception, StockReportOutput>> Junctions() =>
        Chain<CountStockJunction>().Resolve();
}
