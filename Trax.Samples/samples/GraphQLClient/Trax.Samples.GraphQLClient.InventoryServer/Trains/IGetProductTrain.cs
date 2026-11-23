using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GraphQLClient.InventoryServer.Trains;

public interface IGetProductTrain : IServiceTrain<GetProductInput, GetProductOutput>;
