using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public interface ILowStockCheckTrain : IServiceTrain<LowStockCheckInput, LowStockCheckOutput>;
