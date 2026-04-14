using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.UpdateProfile;

public interface IUpdateProfileTrain : IServiceTrain<UpdateProfileInput, UpdateProfileOutput>;
