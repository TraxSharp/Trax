using Trax.Core.Junction;
using Trax.Samples.TestRunner.Services;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects.Junctions;

public class ScanMonorepoJunction(TestProjectRegistry registry)
    : Junction<DiscoverTestProjectsInput, DiscoverTestProjectsOutput>
{
    public override Task<DiscoverTestProjectsOutput> Run(DiscoverTestProjectsInput input)
    {
        return Task.FromResult(
            new DiscoverTestProjectsOutput { Projects = registry.Projects.ToList() }
        );
    }
}
