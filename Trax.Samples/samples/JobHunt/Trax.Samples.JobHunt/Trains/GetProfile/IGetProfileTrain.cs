using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.GetProfile;

public interface IGetProfileTrain : IServiceTrain<GetProfileInput, GetProfileOutput>;
