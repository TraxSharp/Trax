using System.Globalization;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

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
            description: string.Create(
                CultureInfo.InvariantCulture,
                $"Large test input with {dataSizeMB}MB of data"
            )
        );
    }

    /// <summary>
    /// Creates a nested test input with multiple child trains.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="childCount">Number of child trains</param>
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
                    id: string.Create(CultureInfo.InvariantCulture, $"child_{i}"),
                    dataSizeBytes: childDataSizeBytes,
                    description: string.Create(CultureInfo.InvariantCulture, $"Child train {i}")
                )
            )
            .ToList();

        return new NestedTestInput
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            ChildInputs = childInputs,
            Description = string.Create(
                CultureInfo.InvariantCulture,
                $"Nested test with {childCount} children"
            ),
        };
    }

    /// <summary>
    /// Creates a batch of memory test inputs for stress testing.
    /// </summary>
    /// <param name="count">Number of inputs to create</param>
    /// <param name="dataSizeBytes">Size of data for each input</param>
    /// <returns>List of MemoryTestInput instances</returns>
    public static IList<MemoryTestInput> CreateBatch(int count = 100, int dataSizeBytes = 1024 * 10)
    {
        return Enumerable
            .Range(0, count)
            .Select(i =>
                CreateInput(
                    id: string.Create(CultureInfo.InvariantCulture, $"batch_{i:D4}"),
                    dataSizeBytes: dataSizeBytes,
                    description: string.Create(CultureInfo.InvariantCulture, $"Batch item {i}")
                )
            )
            .ToList();
    }
}
