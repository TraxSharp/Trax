using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public interface IFulfilOrderTrain : IServiceTrain<FulfilOrderInput, FulfilOrderOutput>;
