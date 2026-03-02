using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.MemoryLeak.Integration.TestTrains.TestModels;

namespace Trax.Effect.Tests.MemoryLeak.Integration.TestTrains;

/// <summary>
/// Interface for the memory test train.
/// </summary>
public interface IMemoryTestTrain : IServiceTrain<MemoryTestInput, MemoryTestOutput>;

/// <summary>
/// A test train designed to generate memory allocation patterns for leak testing.
/// This train creates large JsonDocument objects to amplify any memory leaks.
/// </summary>
public class MemoryTestTrain : ServiceTrain<MemoryTestInput, MemoryTestOutput>, IMemoryTestTrain
{
    protected override async Task<Either<Exception, MemoryTestOutput>> RunInternal(
        MemoryTestInput input
    )
    {
        try
        {
            // Simulate some processing that might cause memory allocation
            await Task.Delay(input.ProcessingDelayMs);

            // Create a large output object to test JsonDocument serialization
            var largeData = new string('X', input.DataSizeBytes);

            return new MemoryTestOutput
            {
                Id = input.Id,
                ProcessedAt = DateTime.UtcNow,
                ProcessedData = largeData,
                Success = true,
                Message =
                    $"Successfully processed train {input.Id} with {input.DataSizeBytes} bytes of data",
            };
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

/// <summary>
/// A train that intentionally throws exceptions to test error handling memory behavior.
/// </summary>
public interface IFailingTestTrain : IServiceTrain<FailingTestInput, MemoryTestOutput>;

public class FailingTestTrain : ServiceTrain<FailingTestInput, MemoryTestOutput>, IFailingTestTrain
{
    protected override async Task<Either<Exception, MemoryTestOutput>> RunInternal(
        FailingTestInput input
    )
    {
        await Task.Delay(input.ProcessingDelayMs);

        // Always throw an exception to test error handling paths
        return new InvalidOperationException($"Intentional failure in train {input.Id}");
    }
}

/// <summary>
/// A train that creates nested child trains to test hierarchical memory patterns.
/// </summary>
public interface INestedTestTrain : IServiceTrain<NestedTestInput, NestedTestOutput>;

public class NestedTestTrain : ServiceTrain<NestedTestInput, NestedTestOutput>, INestedTestTrain
{
    protected override async Task<Either<Exception, NestedTestOutput>> RunInternal(
        NestedTestInput input
    )
    {
        try
        {
            var results = new List<MemoryTestOutput>();

            // Process each child input sequentially
            foreach (var childInput in input.ChildInputs)
            {
                // Create child train data
                var childResult = new MemoryTestOutput
                {
                    Id = childInput.Id,
                    ProcessedAt = DateTime.UtcNow,
                    ProcessedData = new string('Y', childInput.DataSizeBytes),
                    Success = true,
                    Message = $"Child train {childInput.Id} processed",
                };

                results.Add(childResult);
            }

            return new NestedTestOutput
            {
                Id = input.Id,
                ProcessedAt = DateTime.UtcNow,
                ChildResults = results,
                Success = true,
                Message = $"Processed {results.Count} child trains",
            };
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
