using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects;

public interface IDiscoverTestProjectsTrain
    : IServiceTrain<DiscoverTestProjectsInput, DiscoverTestProjectsOutput>;
