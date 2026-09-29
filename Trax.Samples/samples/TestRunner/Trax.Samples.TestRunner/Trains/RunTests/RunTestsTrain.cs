using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Trains.RunTests;

// Anonymous because the hub that serves it registers no authentication and runs only in
// Development, bound to localhost (see the Hub's Program.cs). The caller names a project and
// ResolveProjectJunction refuses any name the registry did not discover, so this builds and
// runs only the test projects already on this machine. Before exposing it anywhere else,
// replace this with [TraxAuthorize] and a real scheme.
[TraxAllowAnonymous]
[TraxMutation(GraphQLOperation.Queue, Description = "Runs a test project and returns results")]
[TraxBroadcast]
public class RunTestsTrain : ServiceTrain<RunTestsInput, RunTestsOutput>, IRunTestsTrain
{
    protected override Task<Either<Exception, RunTestsOutput>> Junctions() =>
        Chain<ResolveProjectJunction>()
            .Chain<BuildProjectJunction>()
            .Chain<ExecuteTestsJunction>()
            .Resolve();
}
