using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.DiscoverTestProjects.Junctions;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects;

[TraxAllowAnonymous]
[TraxQuery(Description = "Lists all discoverable NUnit test projects")]
public class DiscoverTestProjectsTrain
    : ServiceTrain<DiscoverTestProjectsInput, DiscoverTestProjectsOutput>,
        IDiscoverTestProjectsTrain
{
    protected override Task<Either<Exception, DiscoverTestProjectsOutput>> Junctions() =>
        Chain<ScanProjectsJunction>().Resolve();
}
