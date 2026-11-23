using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public interface ICheckpointedOrderTrain : IServiceTrain<PlaceOrderInput, PlaceOrderOutput>;
