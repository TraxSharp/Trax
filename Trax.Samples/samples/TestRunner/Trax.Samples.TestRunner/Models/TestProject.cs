namespace Trax.Samples.TestRunner.Models;

public record TestProject
{
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
}
