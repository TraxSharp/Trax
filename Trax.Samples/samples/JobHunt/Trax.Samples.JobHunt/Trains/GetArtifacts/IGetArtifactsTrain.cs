using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.GetArtifacts;

public interface IGetArtifactsTrain : IServiceTrain<GetArtifactsInput, GetArtifactsOutput>;
