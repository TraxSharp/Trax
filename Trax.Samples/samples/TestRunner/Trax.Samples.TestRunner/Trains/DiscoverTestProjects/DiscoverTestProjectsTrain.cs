using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.DiscoverTestProjects.Junctions;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects;

[TraxQuery(Description = "Lists all test projects in the Trax monorepo")]
public class DiscoverTestProjectsTrain
    : ServiceTrain<DiscoverTestProjectsInput, DiscoverTestProjectsOutput>,
        IDiscoverTestProjectsTrain
{
    protected override async Task<Either<Exception, DiscoverTestProjectsOutput>> RunInternal(
        DiscoverTestProjectsInput input
    ) => Activate(input).Chain<ScanMonorepoJunction>().Resolve();
}
