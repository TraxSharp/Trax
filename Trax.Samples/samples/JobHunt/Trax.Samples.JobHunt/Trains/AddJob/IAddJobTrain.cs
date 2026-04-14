using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.JobHunt.Trains.AddJob;

public interface IAddJobTrain : IServiceTrain<AddJobInput, AddJobOutput>;
