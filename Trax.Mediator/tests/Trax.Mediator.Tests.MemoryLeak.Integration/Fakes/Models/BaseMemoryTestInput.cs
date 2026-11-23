namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

/// <summary>
/// Base input model for memory test trains.
/// Includes configurable data size to amplify memory allocation patterns.
/// </summary>
public abstract record BaseMemoryTestInput
{
    /// <summary>
    /// Unique identifier for the train execution.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Size of test data to generate in bytes.
    /// Used to create large objects that amplify memory leak patterns.
    /// </summary>
    public int DataSizeBytes { get; init; } = 1024 * 10; // 10KB default

    /// <summary>
    /// Processing delay in milliseconds to simulate work.
    /// </summary>
    public int ProcessingDelayMs { get; init; } = 10;

    /// <summary>
    /// Additional metadata for the test.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Test categories for organizing test scenarios.
    /// </summary>
    public IList<string> Categories { get; init; } = [];
}
