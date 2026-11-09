using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.ReturnOrder;

public interface IReturnOrderTrain : IServiceTrain<ReturnOrderInput, ReturnOrderOutput>;
