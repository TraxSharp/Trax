using Trax.Samples.TestRunner.Models;

namespace Trax.Samples.TestRunner.Trains.RunTests;

public record RunTestsOutput
{
    public required TestResult Result { get; init; }
}
