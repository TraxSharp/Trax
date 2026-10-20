using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Trains.FulfilOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public class FulfilOrderTrain : ServiceTrain<FulfilOrderInput, FulfilOrderOutput>, IFulfilOrderTrain
{
    protected override Task<Either<Exception, FulfilOrderOutput>> Junctions() =>
        Parallel(p =>
                p.Branch("box", b => b.Chain<ChooseBoxJunction>())
                    .Branch("courier", b => b.Chain<GetCourierRatesJunction>())
            )
            .Chain<BuyLabelJunction>()
            // A fulfilment that fails after this resumes here, with the label it bought.
            .Checkpoint<ShippingLabel>()
            .Chain<PackOrderJunction>()
            .Resolve();

    // One order is fulfilled one queued run at a time, however often it is queued.
    protected override string? QueueSubjectKey(Metadata metadata) => $"order:{TrainInput.OrderId}";
}
