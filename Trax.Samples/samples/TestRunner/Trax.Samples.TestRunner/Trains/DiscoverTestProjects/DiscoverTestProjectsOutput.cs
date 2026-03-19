using Trax.Samples.TestRunner.Models;

namespace Trax.Samples.TestRunner.Trains.DiscoverTestProjects;

public record DiscoverTestProjectsOutput
{
    public required List<TestProject> Projects { get; init; }
}
