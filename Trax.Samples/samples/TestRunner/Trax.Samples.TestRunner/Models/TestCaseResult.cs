namespace Trax.Samples.TestRunner.Models;

public record TestCaseResult
{
    public required string FullName { get; init; }
    public required string Outcome { get; init; }
    public double DurationSeconds { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }
}
