namespace Trax.Samples.TestRunner.Models;

public record TestResult
{
    public required string ProjectName { get; init; }
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public double DurationSeconds { get; init; }
    public List<TestCaseResult> FailedTests { get; init; } = [];
}
