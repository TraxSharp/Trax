using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.TestRunner.Trains.DiscoverTestProjects.Junctions;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects;

[TraxQuery(Description = "Lists all discoverable NUnit test projects")]
public class DiscoverTestProjectsTrain
    : ServiceTrain<DiscoverTestProjectsInput, DiscoverTestProjectsOutput>,
        IDiscoverTestProjectsTrain
{
    protected override DiscoverTestProjectsOutput Junctions() => Chain<ScanProjectsJunction>();
}
