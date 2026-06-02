using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Trains.RunTests;

[TraxAllowAnonymous]
[TraxMutation(GraphQLOperation.Queue, Description = "Runs a test project and returns results")]
[TraxBroadcast]
public class RunTestsTrain : ServiceTrain<RunTestsInput, RunTestsOutput>, IRunTestsTrain
{
    protected override Task<Either<Exception, RunTestsOutput>> Junctions() =>
        Chain<BuildProjectJunction>().Chain<ExecuteTestsJunction>().Resolve();
}
