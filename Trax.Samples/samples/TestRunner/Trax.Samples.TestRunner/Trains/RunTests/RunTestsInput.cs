namespace Trax.Samples.TestRunner.Trains.RunTests;

public record RunTestsInput
{
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public bool Build { get; init; } = true;
}
