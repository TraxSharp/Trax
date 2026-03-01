namespace Trax.Effect.Tests.MemoryLeak.Integration.TestWorkflows.TestModels;

/// <summary>
/// Base input model for memory test workflows.
/// Includes configurable data size to amplify memory allocation patterns.
/// </summary>
public abstract record BaseMemoryTestInput
{
    /// <summary>
    /// Unique identifier for the workflow execution.
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
    public List<string> Categories { get; init; } = [];
}

/// <summary>
/// Input model for the standard memory test workflow.
/// </summary>
public record MemoryTestInput : BaseMemoryTestInput;

/// <summary>
/// Input model for the failing test workflow.
/// </summary>
public record FailingTestInput : BaseMemoryTestInput;

/// <summary>
/// Output model for memory test workflows.
/// Contains large data objects to test JsonDocument serialization and disposal.
/// </summary>
public record MemoryTestOutput
{
    /// <summary>
    /// Unique identifier for the workflow execution.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Timestamp when the workflow was processed.
    /// </summary>
    public DateTime ProcessedAt { get; init; }

    /// <summary>
    /// Large data object to test memory behavior.
    /// This will be serialized into JsonDocument and should be properly disposed.
    /// </summary>
    public required string ProcessedData { get; init; }

    /// <summary>
    /// Indicates if the workflow completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result message from the workflow.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Additional result metadata.
    /// </summary>
    public Dictionary<string, object> ResultMetadata { get; init; } = [];
}

/// <summary>
/// Input model for nested workflow tests.
/// Used to test hierarchical workflow memory patterns.
/// </summary>
public record NestedTestInput
{
    /// <summary>
    /// Unique identifier for the parent workflow.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Collection of child workflow inputs.
    /// </summary>
    public required List<MemoryTestInput> ChildInputs { get; init; }

    /// <summary>
    /// Description of the nested test scenario.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Output model for nested workflow tests.
/// </summary>
public record NestedTestOutput
{
    /// <summary>
    /// Unique identifier for the parent workflow.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Timestamp when the workflow was processed.
    /// </summary>
    public DateTime ProcessedAt { get; init; }

    /// <summary>
    /// Results from all child workflows.
    /// </summary>
    public required List<MemoryTestOutput> ChildResults { get; init; }

    /// <summary>
    /// Indicates if the workflow completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result message from the workflow.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Static factory class for creating test model instances.
/// </summary>
public static class MemoryTestModelFactory
{
    /// <summary>
    /// Creates a memory test input with the specified parameters.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="dataSizeBytes">Size of test data in bytes</param>
    /// <param name="processingDelayMs">Processing delay in milliseconds</param>
    /// <param name="description">Optional description</param>
    /// <returns>Configured MemoryTestInput</returns>
    public static MemoryTestInput CreateInput(
        string? id = null,
        int dataSizeBytes = 1024 * 10,
        int processingDelayMs = 10,
        string? description = null
    )
    {
        return new MemoryTestInput
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            DataSizeBytes = dataSizeBytes,
            ProcessingDelayMs = processingDelayMs,
            Description = description,
        };
    }

    /// <summary>
    /// Creates a failing test input with the specified parameters.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="dataSizeBytes">Size of test data in bytes</param>
    /// <param name="processingDelayMs">Processing delay in milliseconds</param>
    /// <param name="description">Optional description</param>
    /// <returns>Configured FailingTestInput</returns>
    public static FailingTestInput CreateFailingInput(
        string? id = null,
        int dataSizeBytes = 1024 * 10,
        int processingDelayMs = 10,
        string? description = null
    )
    {
        return new FailingTestInput
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            DataSizeBytes = dataSizeBytes,
            ProcessingDelayMs = processingDelayMs,
            Description = description,
        };
    }

    /// <summary>
    /// Creates a large memory test input designed to amplify memory leaks.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="dataSizeMB">Size of test data in megabytes</param>
    /// <returns>Configured MemoryTestInput with large data size</returns>
    public static MemoryTestInput CreateLargeInput(string? id = null, int dataSizeMB = 1)
    {
        return CreateInput(
            id: id,
            dataSizeBytes: dataSizeMB * 1024 * 1024,
            processingDelayMs: 50,
            description: $"Large test input with {dataSizeMB}MB of data"
        );
    }

    /// <summary>
    /// Creates a nested test input with multiple child workflows.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="childCount">Number of child workflows</param>
    /// <param name="childDataSizeBytes">Size of data for each child</param>
    /// <returns>Configured NestedTestInput</returns>
    public static NestedTestInput CreateNestedInput(
        string? id = null,
        int childCount = 5,
        int childDataSizeBytes = 1024 * 5
    )
    {
        var childInputs = Enumerable
            .Range(0, childCount)
            .Select(i =>
                CreateInput(
                    id: $"child_{i}",
                    dataSizeBytes: childDataSizeBytes,
                    description: $"Child workflow {i}"
                )
            )
            .ToList();

        return new NestedTestInput
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            ChildInputs = childInputs,
            Description = $"Nested test with {childCount} children",
        };
    }

    /// <summary>
    /// Creates a batch of memory test inputs for stress testing.
    /// </summary>
    /// <param name="count">Number of inputs to create</param>
    /// <param name="dataSizeBytes">Size of data for each input</param>
    /// <returns>List of MemoryTestInput instances</returns>
    public static List<MemoryTestInput> CreateBatch(int count = 100, int dataSizeBytes = 1024 * 10)
    {
        return Enumerable
            .Range(0, count)
            .Select(i =>
                CreateInput(
                    id: $"batch_{i:D4}",
                    dataSizeBytes: dataSizeBytes,
                    description: $"Batch item {i}"
                )
            )
            .ToList();
    }
}
