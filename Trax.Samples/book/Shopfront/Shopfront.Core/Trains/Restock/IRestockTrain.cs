using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public interface IRestockTrain : IServiceTrain<RestockInput, RestockOutput>;
