namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

/// <summary>
/// Output model for nested train tests.
/// </summary>
public record NestedTestOutput
{
    /// <summary>
    /// Unique identifier for the parent train.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Timestamp when the train was processed.
    /// </summary>
    public DateTime ProcessedAt { get; init; }

    /// <summary>
    /// Results from all child trains.
    /// </summary>
    public required IList<MemoryTestOutput> ChildResults { get; init; }

    /// <summary>
    /// Indicates if the train completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result message from the train.
    /// </summary>
    public required string Message { get; init; }
}
