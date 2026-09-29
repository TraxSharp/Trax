namespace Trax.Samples.TestRunner.Trains.RunTests;

/// <summary>
/// Names a test project to build and run. The caller supplies a name only, never a path:
/// <see cref="Junctions.ResolveProjectJunction"/> resolves it against the projects
/// <see cref="Services.TestProjectRegistry"/> discovered under the configured root, and refuses
/// any other name.
/// </summary>
public record RunTestsInput
{
    public required string ProjectName { get; init; }
    public bool Build { get; init; } = true;
}
