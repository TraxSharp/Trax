using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

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
    protected override Task<Either<Exception, MemoryTestOutput>> Junctions() =>
        Chain<AllocateMemoryTestOutput>().Resolve();
}

/// <summary>
/// A train that intentionally throws exceptions to test error handling memory behavior.
/// </summary>
public interface IFailingTestTrain : IServiceTrain<FailingTestInput, MemoryTestOutput>;

public class FailingTestTrain : ServiceTrain<FailingTestInput, MemoryTestOutput>, IFailingTestTrain
{
    protected override Task<Either<Exception, MemoryTestOutput>> Junctions() =>
        Chain<FailAfterDelay>().Resolve();
}

/// <summary>
/// A train that creates nested child trains to test hierarchical memory patterns.
/// </summary>
public interface INestedTestTrain : IServiceTrain<NestedTestInput, NestedTestOutput>;

public class NestedTestTrain : ServiceTrain<NestedTestInput, NestedTestOutput>, INestedTestTrain
{
    protected override Task<Either<Exception, NestedTestOutput>> Junctions() =>
        Chain<AllocateNestedTestOutput>().Resolve();
}

/// <summary>
/// Allocates the large payload the leak tests measure. The work sits in a junction because a
/// train declares which junctions run, it does not do the work itself.
/// </summary>
internal sealed class AllocateMemoryTestOutput : Junction<MemoryTestInput, MemoryTestOutput>
{
    public override async Task<MemoryTestOutput> Run(MemoryTestInput input)
    {
        // allowed-delay: the delay is the allocation pattern under test, not a wait for a signal.
        await Task.Delay(input.ProcessingDelayMs, CancellationToken);

        return new MemoryTestOutput
        {
            Id = input.Id,
            ProcessedAt = DateTime.UtcNow,
            ProcessedData = new string('X', input.DataSizeBytes),
            Success = true,
            Message =
                $"Successfully processed train {input.Id} with {input.DataSizeBytes} bytes of data",
        };
    }
}

/// <summary>Throws after a delay, so the error path allocates the way a real failure would.</summary>
internal sealed class FailAfterDelay : Junction<FailingTestInput, MemoryTestOutput>
{
    public override async Task<MemoryTestOutput> Run(FailingTestInput input)
    {
        // allowed-delay: the delay is the allocation pattern under test, not a wait for a signal.
        await Task.Delay(input.ProcessingDelayMs, CancellationToken);

        throw new InvalidOperationException($"Intentional failure in train {input.Id}");
    }
}

/// <summary>Builds one child payload per declared child input.</summary>
internal sealed class AllocateNestedTestOutput : Junction<NestedTestInput, NestedTestOutput>
{
    public override Task<NestedTestOutput> Run(NestedTestInput input)
    {
        var results = input
            .ChildInputs.Select(child => new MemoryTestOutput
            {
                Id = child.Id,
                ProcessedAt = DateTime.UtcNow,
                ProcessedData = new string('Y', child.DataSizeBytes),
                Success = true,
                Message = $"Child train {child.Id} processed",
            })
            .ToList();

        return Task.FromResult(
            new NestedTestOutput
            {
                Id = input.Id,
                ProcessedAt = DateTime.UtcNow,
                ChildResults = results,
                Success = true,
                Message = $"Processed {results.Count} child trains",
            }
        );
    }
}
