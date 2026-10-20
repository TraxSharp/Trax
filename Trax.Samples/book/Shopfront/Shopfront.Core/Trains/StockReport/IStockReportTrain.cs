using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public interface IStockReportTrain : IServiceTrain<StockReportInput, StockReportOutput>;
