using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public interface IPlaceOrderTrain : IServiceTrain<PlaceOrderInput, PlaceOrderOutput>;
