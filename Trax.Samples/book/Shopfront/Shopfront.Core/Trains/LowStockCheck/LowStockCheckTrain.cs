using Shopfront.Trains.LowStockCheck.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public class LowStockCheckTrain
    : ServiceTrain<LowStockCheckInput, LowStockCheckOutput>,
        ILowStockCheckTrain
{
    protected override Task<Either<Exception, LowStockCheckOutput>> Junctions() =>
        Chain<FindLowStockJunction>().Chain<NotifyStaffJunction>().Resolve();
}
