using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating core Trax.Core Train memory management.
/// These tests focus on the Memory dictionary lifecycle and potential memory leaks
/// in the core train execution engine.
/// </summary>
[TestFixture]
public class CoreTrainMemoryTests
{
    [Test]
    public async Task Train_ShouldNotRetainMemoryDictionary_AfterCompletion()
    {
        // This test validates that the Memory dictionary doesn't cause memory leaks
        var trainFactory = () => new LargeDataTrain();
        var trainReferences = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                // Create multiple train instances with large data
                for (int i = 0; i < 50; i++)
                {
                    var train = trainFactory();

                    trainReferences.Add(new WeakReference(train));
                    var largeInput = new LargeDataModel($"test_{i}", new byte[100_000]); // 100KB each

                    var output = await train.Run(largeInput).ConfigureAwait(false);
                    output.Should().NotBeNull();

                    // Train goes out of scope here, but Memory dictionary might retain objects
                }
            },
            "CoreTrain_MemoryDictionary_Retention"
        );

        Console.WriteLine(result.GetSummary());

        // Whether the Memory dictionary let go is a question about reachability, so it is asked of
        // the trains. Bounding MemoryRetained against MemoryAllocated cannot answer it: both sides
        // are the same process-wide measurement and the ratio tightens whenever a collection lands
        // inside the window.
        var aliveTrains = await MemoryProfiler.CountAliveAsync(trainReferences);

        aliveTrains
            .Should()
            .Be(
                0,
                "each train and the Memory dictionary holding its 100KB input went out of scope, so "
                    + "one still reachable is the retention this test exists to catch"
            );

        // Should not retain more than 10MB after processing 50x100KB trains
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "Should not retain significant memory from completed trains"
            );
    }

    [Test]
    public async Task Train_MemoryDictionary_ShouldGrowWithJunctionCount()
    {
        // Allocation grows with junction count: LargeChainTrain chains five junctions to SmallChainTrain's
        // one, so it stores more entries in the Memory dictionary and allocates more per run.
        //
        // Measuring that deterministically needs two things the old net-heap-delta approach got wrong. Warm up
        // both trains first, so the measured runs reflect steady-state allocation rather than one-time JIT /
        // type-init / DI-resolution cost (that cost inflated whichever train ran first, which made the
        // comparison order-dependent and flaky). And measure real bytes allocated via GetTotalAllocatedBytes,
        // which is monotonic and so unaffected by GC timing, amortised over many iterations to drown per-run
        // noise.
        await new SmallChainTrain().Run(new SimpleInput("warmup"));
        await new LargeChainTrain().Run(new SimpleInput("warmup"));

        const int iterations = 200;
        var smallAllocated = await AllocatedOverAsync(
            () => new SmallChainTrain().Run(new SimpleInput("small_test")),
            iterations
        );
        var largeAllocated = await AllocatedOverAsync(
            () => new LargeChainTrain().Run(new SimpleInput("large_test")),
            iterations
        );

        Console.WriteLine($"SmallChainTrain: {smallAllocated:N0} bytes over {iterations} runs");
        Console.WriteLine($"LargeChainTrain: {largeAllocated:N0} bytes over {iterations} runs");

        largeAllocated
            .Should()
            .BeGreaterThan(
                smallAllocated,
                "a train with more junctions allocates more (its Memory dictionary holds more entries)"
            );
    }

    // Real bytes allocated while running `body` `iterations` times. GC.GetTotalAllocatedBytes is cumulative
    // and monotonic, so the delta is the allocation total independent of when the GC ran, unlike a heap-size
    // snapshot, which shrinks whenever a collection happens mid-measure.
    private static async Task<long> AllocatedOverAsync(Func<Task> body, int iterations)
    {
        var start = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++)
            await body().ConfigureAwait(false);
        return GC.GetTotalAllocatedBytes(precise: true) - start;
    }

    [Test]
    public async Task Train_TupleStorage_ShouldNotMultiplyReferences()
    {
        // Test tuple storage behavior in Memory dictionary
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var train = new TupleTrain();
                    var input = new SimpleInput($"tuple_test_{i}");
                    var output = await train.Run(input).ConfigureAwait(false);
                    output.Should().NotBeNull();
                }
            },
            "TupleTrain_MemoryUsage"
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
    public async Task Train_WithLargeObjects_ShouldReleaseMemory()
    {
        // Test train behavior with very large objects
        var largeObjectTrains = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    var train = new VeryLargeDataTrain();
                    largeObjectTrains.Add(new WeakReference(train));

                    var largeInput = new VeryLargeDataModel($"large_{i}", new byte[1_000_000]); // 1MB each
                    var output = await train.Run(largeInput).ConfigureAwait(false);
                    output.Should().NotBeNull();
                }
            },
            "VeryLargeDataTrain_MemoryUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Force GC and check if trains can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100); // Give GC time to work

        var aliveTrains = largeObjectTrains.Count(wr => wr.IsAlive);
        Console.WriteLine($"Trains still alive after GC: {aliveTrains}/{largeObjectTrains.Count}");

        aliveTrains
            .Should()
            .BeLessThan(
                largeObjectTrains.Count,
                "Some trains should be collected by GC after going out of scope"
            );

        // No ratio of MemoryAllocated; the reachability check above is the claim. See
        // MemoryProfiler.MemoryRetained.
    }

    [Test]
    public async Task MultipleTrains_Concurrent_ShouldNotLeakMemory()
    {
        // Test concurrent train execution for memory leaks
        const int concurrentTrains = 20;
        const int executionsPerTrain = 5;

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var tasks = Enumerable
                    .Range(0, concurrentTrains)
                    .Select(async trainId =>
                    {
                        for (int i = 0; i < executionsPerTrain; i++)
                        {
                            var train = new LargeDataTrain();
                            var input = new LargeDataModel(
                                $"concurrent_{trainId}_{i}",
                                new byte[50_000]
                            ); // 50KB each
                            var output = await train.Run(input).ConfigureAwait(false);
                            output.Should().NotBeNull();
                        }
                    });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            },
            "ConcurrentTrains_MemoryUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Concurrent execution should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                15 * 1024 * 1024,
                "Concurrent train execution should not cause significant memory leaks"
            );
    }

    [Test]
    public void Train_MemoryDictionary_ShouldAllowManualClearing()
    {
        // Test if we can manually clear the Memory dictionary (future enhancement)
        var train = new TestableTrain();
        var input = new SimpleInput("clear_test");

        // Run train to populate Memory
        var result = train.Run(input).Result;
        result.Should().NotBeNull();

        // Memory dictionary should contain objects
        train
            .GetMemoryCount()
            .Should()
            .BeGreaterThan(0, "Memory dictionary should contain objects after train execution");

        // Manual clear (this would be a future enhancement)
        train.ClearMemory();

        train
            .GetMemoryCount()
            .Should()
            .Be(0, "Memory dictionary should be empty after manual clear");
    }

    [Test]
    public async Task RepeatedTrainExecution_ShouldShowConsistentMemoryUsage()
    {
        // Test repeated execution of the same train for memory consistency
        var batchResults = new List<MemoryMonitorResult>();

        for (int batch = 0; batch < 3; batch++)
        {
            var result = await MemoryProfiler.MonitorMemoryUsageAsync(
                async () =>
                {
                    for (int i = 0; i < 15; i++)
                    {
                        var train = new LargeDataTrain();
                        var input = new LargeDataModel($"batch_{batch}_item_{i}", new byte[75_000]); // 75KB each
                        var output = await train.Run(input).ConfigureAwait(false);
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
