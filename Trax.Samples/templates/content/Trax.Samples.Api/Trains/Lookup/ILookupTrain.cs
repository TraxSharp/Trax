using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Api.Trains.Lookup;

public interface ILookupTrain : IServiceTrain<LookupInput, LookupOutput>;
