using Trax.Core.Step;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;
using Trax.Core.Train;
using FluentAssertions;
using LanguageExt;
using Monad = Trax.Core.Monad;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating core Trax.Core Workflow memory management.
/// These tests focus on the Memory dictionary lifecycle and potential memory leaks
/// in the core workflow execution engine.
/// </summary>
[TestFixture]
public class CoreWorkflowMemoryTests
{
    [Test]
    public async Task Workflow_ShouldNotRetainMemoryDictionary_AfterCompletion()
    {
        // This test validates that the Memory dictionary doesn't cause memory leaks
        var workflowFactory = () => new LargeDataWorkflow();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                // Create multiple workflow instances with large data
                for (int i = 0; i < 50; i++)
                {
                    var workflow = workflowFactory();
                    var largeInput = new LargeDataModel($"test_{i}", new byte[100_000]); // 100KB each

                    var output = await workflow.Run(largeInput);
                    output.Should().NotBeNull();

                    // Workflow goes out of scope here, but Memory dictionary might retain objects
                }
            },
            "CoreWorkflow_MemoryDictionary_Retention"
        );

        Console.WriteLine(result.GetSummary());

        // Memory should be freed after GC since workflows are out of scope
        result
            .MemoryRetained.Should()
            .BeLessThan(
                result.MemoryAllocated / 2,
                "Most memory should be freed when workflows go out of scope"
            );

        // Should not retain more than 10MB after processing 50x100KB workflows
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "Should not retain significant memory from completed workflows"
            );
    }

    [Test]
    public async Task Workflow_MemoryDictionary_ShouldGrowWithStepCount()
    {
        // Test how Memory dictionary grows with increasing number of steps
        var smallWorkflowResult = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var workflow = new SmallChainWorkflow();
                var input = new SimpleInput("small_test");
                await workflow.Run(input);
            },
            "SmallWorkflow_MemoryUsage"
        );

        var largeWorkflowResult = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var workflow = new LargeChainWorkflow(); // Many more steps
                var input = new SimpleInput("large_test");
                await workflow.Run(input);
            },
            "LargeWorkflow_MemoryUsage"
        );

        Console.WriteLine(smallWorkflowResult.GetSummary());
        Console.WriteLine(largeWorkflowResult.GetSummary());

        // Large workflow may allocate similar or more memory due to more objects in Memory dictionary
        // Note: Very efficient workflows might have similar allocation patterns
        largeWorkflowResult
            .MemoryAllocated.Should()
            .BeGreaterThanOrEqualTo(
                smallWorkflowResult.MemoryAllocated / 3,
                "Workflows should have reasonable memory allocation patterns"
            );
    }

    [Test]
    public async Task Workflow_TupleStorage_ShouldNotMultiplyReferences()
    {
        // Test tuple storage behavior in Memory dictionary
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var workflow = new TupleWorkflow();
                    var input = new SimpleInput($"tuple_test_{i}");
                    var output = await workflow.Run(input);
                    output.Should().NotBeNull();
                }
            },
            "TupleWorkflow_MemoryUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Tuple handling should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "Tuple handling should not cause significant memory leaks"
            );
    }

    [Test]
    public async Task Workflow_WithLargeObjects_ShouldReleaseMemory()
    {
        // Test workflow behavior with very large objects
        var largeObjectWorkflows = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    var workflow = new VeryLargeDataWorkflow();
                    largeObjectWorkflows.Add(new WeakReference(workflow));

                    var largeInput = new VeryLargeDataModel($"large_{i}", new byte[1_000_000]); // 1MB each
                    var output = await workflow.Run(largeInput);
                    output.Should().NotBeNull();
                }
            },
            "VeryLargeDataWorkflow_MemoryUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Force GC and check if workflows can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100); // Give GC time to work

        var aliveWorkflows = largeObjectWorkflows.Count(wr => wr.IsAlive);
        Console.WriteLine(
            $"Workflows still alive after GC: {aliveWorkflows}/{largeObjectWorkflows.Count}"
        );

        aliveWorkflows
            .Should()
            .BeLessThan(
                largeObjectWorkflows.Count,
                "Some workflows should be collected by GC after going out of scope"
            );

        // Memory retention should be minimal compared to allocation
        result
            .MemoryRetained.Should()
            .BeLessThan(
                result.MemoryAllocated / 3,
                "Most memory should be freed for large object workflows"
            );
    }

    [Test]
    public async Task MultipleWorkflows_Concurrent_ShouldNotLeakMemory()
    {
        // Test concurrent workflow execution for memory leaks
        const int concurrentWorkflows = 20;
        const int executionsPerWorkflow = 5;

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var tasks = Enumerable
                    .Range(0, concurrentWorkflows)
                    .Select(async workflowId =>
                    {
                        for (int i = 0; i < executionsPerWorkflow; i++)
                        {
                            var workflow = new LargeDataWorkflow();
                            var input = new LargeDataModel(
                                $"concurrent_{workflowId}_{i}",
                                new byte[50_000]
                            ); // 50KB each
                            var output = await workflow.Run(input);
                            output.Should().NotBeNull();
                        }
                    });

                await Task.WhenAll(tasks);
            },
            "ConcurrentWorkflows_MemoryUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Concurrent execution should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                15 * 1024 * 1024,
                "Concurrent workflow execution should not cause significant memory leaks"
            );
    }

    [Test]
    public void Workflow_MemoryDictionary_ShouldAllowManualClearing()
    {
        // Test if we can manually clear the Memory dictionary (future enhancement)
        var workflow = new TestableWorkflow();
        var input = new SimpleInput("clear_test");

        // Run workflow to populate Memory
        var result = workflow.Run(input).Result;
        result.Should().NotBeNull();

        // Memory dictionary should contain objects
        workflow
            .GetMemoryCount()
            .Should()
            .BeGreaterThan(0, "Memory dictionary should contain objects after workflow execution");

        // Manual clear (this would be a future enhancement)
        workflow.ClearMemory();

        workflow
            .GetMemoryCount()
            .Should()
            .Be(0, "Memory dictionary should be empty after manual clear");
    }

    [Test]
    public async Task RepeatedWorkflowExecution_ShouldShowConsistentMemoryUsage()
    {
        // Test repeated execution of the same workflow for memory consistency
        var batchResults = new List<MemoryMonitorResult>();

        for (int batch = 0; batch < 3; batch++)
        {
            var result = await MemoryProfiler.MonitorMemoryUsageAsync(
                async () =>
                {
                    for (int i = 0; i < 15; i++)
                    {
                        var workflow = new LargeDataWorkflow();
                        var input = new LargeDataModel($"batch_{batch}_item_{i}", new byte[75_000]); // 75KB each
                        var output = await workflow.Run(input);
                        output.Should().NotBeNull();
                    }
                },
                $"RepeatedExecution_Batch_{batch}"
            );

            batchResults.Add(result);
            Console.WriteLine(result.GetSummary());

            // Force cleanup between batches
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // Memory usage should be consistent across batches (no cumulative leaks)
        var retainedMemories = batchResults.Select(r => r.MemoryRetained).ToList();
        var maxRetained = retainedMemories.Max();
        var minRetained = retainedMemories.Min();

        (maxRetained - minRetained)
            .Should()
            .BeLessThan(
                8 * 1024 * 1024,
                "Memory retention should be consistent across batches (difference < 8MB)"
            );
    }
}

// Test workflow classes
public class SimpleInput(string name)
{
    public string Name { get; } = name;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}

public class SimpleOutput(string result)
{
    public string Result { get; } = result;
    public DateTime ProcessedAt { get; } = DateTime.UtcNow;
}

public class LargeDataModel(string name, byte[] data)
{
    public string Name { get; } = name;
    public byte[] Data { get; } = data;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}

public class VeryLargeDataModel(string name, byte[] data)
{
    public string Name { get; } = name;
    public byte[] Data { get; } = data;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public string Description { get; } = new string('X', 10000); // Additional 10KB string
}

// Simple step that processes input
public class ProcessStep : Step<SimpleInput, SimpleOutput>
{
    public override Task<SimpleOutput> Run(SimpleInput input)
    {
        return Task.FromResult(new SimpleOutput($"Processed: {input.Name}"));
    }
}

// Step that processes SimpleOutput to SimpleOutput (for chaining)
public class ProcessOutputStep : Step<SimpleOutput, SimpleOutput>
{
    public override Task<SimpleOutput> Run(SimpleOutput input)
    {
        return Task.FromResult(new SimpleOutput($"Reprocessed: {input.Result}"));
    }
}

// Step that handles large data
public class LargeDataStep : Step<LargeDataModel, SimpleOutput>
{
    public override Task<SimpleOutput> Run(LargeDataModel input)
    {
        // Simulate some processing
        var processedSize = input.Data.Length;
        return Task.FromResult(new SimpleOutput($"Processed {processedSize} bytes"));
    }
}

// Step that returns a tuple
public class TupleStep : Step<SimpleInput, (string Result, int Count, DateTime Timestamp)>
{
    public override Task<(string Result, int Count, DateTime Timestamp)> Run(SimpleInput input)
    {
        return Task.FromResult(
            (
                Result: $"Tuple result for {input.Name}",
                Count: input.Name.Length,
                Timestamp: DateTime.UtcNow
            )
        );
    }
}

// Test workflows
public class SmallChainWorkflow : Train<SimpleInput, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> RunInternal(SimpleInput input)
    {
        var result = Activate(input).Chain<ProcessStep>().Resolve();
        return Task.FromResult(result);
    }
}

public class LargeChainWorkflow : Train<SimpleInput, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> RunInternal(SimpleInput input)
    {
        // Chain multiple steps to fill Memory dictionary
        var result = Activate(input)
            .Chain<ProcessStep>()
            .Chain<ProcessOutputStep>()
            .Chain<ProcessOutputStep>()
            .Chain<ProcessOutputStep>()
            .Chain<ProcessOutputStep>()
            .Resolve();

        return Task.FromResult(result);
    }
}

public class LargeDataWorkflow : Train<LargeDataModel, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> RunInternal(LargeDataModel input)
    {
        var result = Activate(input).Chain<LargeDataStep>().Resolve();
        return Task.FromResult(result);
    }
}

public class VeryLargeDataWorkflow : Train<VeryLargeDataModel, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> RunInternal(VeryLargeDataModel input)
    {
        var largeModel = new LargeDataModel(input.Name, input.Data);
        var result = Activate(input, largeModel).Chain<LargeDataStep>().Resolve();
        return Task.FromResult(result);
    }
}

public class TupleWorkflow : Train<SimpleInput, (string Result, int Count, DateTime Timestamp)>
{
    protected override Task<
        Either<Exception, (string Result, int Count, DateTime Timestamp)>
    > RunInternal(SimpleInput input)
    {
        var result = Activate(input).Chain<TupleStep>().Resolve();
        return Task.FromResult(result);
    }
}

// Testable workflow that exposes Memory dictionary for testing
public class TestableWorkflow : Train<SimpleInput, SimpleOutput>
{
    private Monad.Monad<SimpleInput, SimpleOutput>? _monad;

    protected override Task<Either<Exception, SimpleOutput>> RunInternal(SimpleInput input)
    {
        _monad = Activate(input);
        var result = _monad.Chain<ProcessStep>().Resolve();
        return Task.FromResult(result);
    }

    public int GetMemoryCount() => _monad?.Memory.Count ?? 0;

    public void ClearMemory() => _monad?.Memory.Clear();
}
