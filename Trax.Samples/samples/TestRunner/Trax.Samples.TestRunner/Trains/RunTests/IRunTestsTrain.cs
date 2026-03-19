using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.TestRunner.Trains.RunTests;

public interface IRunTestsTrain : IServiceTrain<RunTestsInput, RunTestsOutput>;
