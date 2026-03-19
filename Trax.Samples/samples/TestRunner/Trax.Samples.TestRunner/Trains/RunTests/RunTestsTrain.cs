using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Trains.RunTests;

[TraxMutation(GraphQLOperation.Queue, Description = "Runs a test project and returns results")]
[TraxBroadcast]
public class RunTestsTrain : ServiceTrain<RunTestsInput, RunTestsOutput>, IRunTestsTrain
{
    protected override async Task<Either<Exception, RunTestsOutput>> RunInternal(
        RunTestsInput input
    ) => Activate(input).Chain<BuildProjectJunction>().Chain<ExecuteTestsJunction>().Resolve();
}
