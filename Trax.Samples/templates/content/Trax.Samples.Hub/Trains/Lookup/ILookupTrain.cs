using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Hub.Trains.Lookup;

public interface ILookupTrain : IServiceTrain<LookupInput, LookupOutput>;
