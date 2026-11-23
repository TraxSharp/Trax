namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

/// <summary>
/// Output model for memory test trains.
/// Contains large data objects to test JsonDocument serialization and disposal.
/// </summary>
public record MemoryTestOutput
{
    /// <summary>
    /// Unique identifier for the train execution.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Timestamp when the train was processed.
    /// </summary>
    public DateTime ProcessedAt { get; init; }

    /// <summary>
    /// Large data object to test memory behavior.
    /// This will be serialized into JsonDocument and should be properly disposed.
    /// </summary>
    public required string ProcessedData { get; init; }

    /// <summary>
    /// Indicates if the train completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result message from the train.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Additional result metadata.
    /// </summary>
    public IDictionary<string, object> ResultMetadata { get; init; } =
        new Dictionary<string, object>(StringComparer.Ordinal);
}
