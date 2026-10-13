using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Order;

public interface IDeliverOrderTrain : IServiceTrain<OrderInput, OrderDelivered>;
