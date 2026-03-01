using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.MemoryLeak.Integration.TestWorkflows.TestModels;

namespace Trax.Effect.Tests.MemoryLeak.Integration.TestWorkflows;

/// <summary>
/// Interface for the memory test workflow.
/// </summary>
public interface IMemoryTestWorkflow : IServiceTrain<MemoryTestInput, MemoryTestOutput>;

/// <summary>
/// A test workflow designed to generate memory allocation patterns for leak testing.
/// This workflow creates large JsonDocument objects to amplify any memory leaks.
/// </summary>
public class MemoryTestWorkflow
    : ServiceTrain<MemoryTestInput, MemoryTestOutput>,
        IMemoryTestWorkflow
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
                    $"Successfully processed workflow {input.Id} with {input.DataSizeBytes} bytes of data",
            };
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}

/// <summary>
/// A workflow that intentionally throws exceptions to test error handling memory behavior.
/// </summary>
public interface IFailingTestWorkflow : IServiceTrain<FailingTestInput, MemoryTestOutput>;

public class FailingTestWorkflow
    : ServiceTrain<FailingTestInput, MemoryTestOutput>,
        IFailingTestWorkflow
{
    protected override async Task<Either<Exception, MemoryTestOutput>> RunInternal(
        FailingTestInput input
    )
    {
        await Task.Delay(input.ProcessingDelayMs);

        // Always throw an exception to test error handling paths
        return new InvalidOperationException($"Intentional failure in workflow {input.Id}");
    }
}

/// <summary>
/// A workflow that creates nested child workflows to test hierarchical memory patterns.
/// </summary>
public interface INestedTestWorkflow : IServiceTrain<NestedTestInput, NestedTestOutput>;

public class NestedTestWorkflow
    : ServiceTrain<NestedTestInput, NestedTestOutput>,
        INestedTestWorkflow
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
                // Create child workflow data
                var childResult = new MemoryTestOutput
                {
                    Id = childInput.Id,
                    ProcessedAt = DateTime.UtcNow,
                    ProcessedData = new string('Y', childInput.DataSizeBytes),
                    Success = true,
                    Message = $"Child workflow {childInput.Id} processed",
                };

                results.Add(childResult);
            }

            return new NestedTestOutput
            {
                Id = input.Id,
                ProcessedAt = DateTime.UtcNow,
                ChildResults = results,
                Success = true,
                Message = $"Processed {results.Count} child workflows",
            };
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
