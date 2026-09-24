using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fixtures;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating TrainBus reflection method caching performance and memory efficiency.
/// This addresses the reflection performance issue that was causing memory pressure.
/// </summary>
[TestFixture]
public class TrainBusReflectionCacheTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        // Clear the static cache before each test to prevent memory leaks
        TrainBus.ClearMethodCache();
        _serviceProvider = TestSetup.CreateMemoryOnlyTestServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Clear the static cache after each test to prevent memory leaks
        TrainBus.ClearMethodCache();

        // Force garbage collection to ensure cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// The absolute time a warm, cached execution is allowed to take when the baseline it is being
    /// compared against was itself fast. Generous on purpose: these runs are in-memory, so a warm
    /// execution is normally well under a millisecond, and the floor exists to stop a quick baseline
    /// from setting an unmeetable bound rather than to pin performance.
    /// </summary>
    private const double ComparableExecutionFloorMs = 10;

    [Test]
    public async Task TrainBus_ShouldCacheReflectionLookups()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput();

        // Warm up the cache with first execution
        await trainBus.RunAsync<MemoryTestOutput>(input);

        // Act - Measure subsequent executions that should use cached methods
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            var testInput = MemoryTestModelFactory.CreateInput($"cache_test_{i}");
            var result = await trainBus.RunAsync<MemoryTestOutput>(testInput);
            result.Should().NotBeNull();
        }

        stopwatch.Stop();

        // Assert - Cached executions should be fast
        var averageMs = stopwatch.ElapsedMilliseconds / 100.0;
        Console.WriteLine($"Average execution time with caching: {averageMs:F2}ms");

        averageMs
            .Should()
            .BeLessThan(
                50,
                "Cached reflection lookups should make executions faster than 50ms on average"
            );
    }

    [Test]
    public async Task TrainBus_ShouldNotLeakMemory_WithRepeatedExecutions()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act & Assert
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                // Execute many trains to test reflection caching memory behavior
                for (int i = 0; i < 200; i++)
                {
                    var testInput = MemoryTestModelFactory.CreateInput($"reflection_test_{i}");
                    var output = await trainBus.RunAsync<MemoryTestOutput>(testInput);
                    output.Should().NotBeNull();
                }
            },
            "TrainBus_RepeatedExecutions_MemoryTest"
        );

        Console.WriteLine(result.GetSummary());

        // With reflection caching, memory retention should be minimal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "Reflection caching should prevent significant memory retention (< 5MB)"
            );
    }

    [Test]
    public async Task TrainBus_ShouldHandleConcurrentExecutions_WithoutMemoryLeaks()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        const int concurrentTasks = 20;
        const int executionsPerTask = 10;

        // Act & Assert
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var tasks = Enumerable
                    .Range(0, concurrentTasks)
                    .Select(async taskId =>
                    {
                        for (int i = 0; i < executionsPerTask; i++)
                        {
                            var testInput = MemoryTestModelFactory.CreateInput(
                                $"concurrent_{taskId}_{i}"
                            );
                            var output = await trainBus.RunAsync<MemoryTestOutput>(testInput);
                            output.Should().NotBeNull();
                        }
                    });

                await Task.WhenAll(tasks);
            },
            "TrainBus_ConcurrentExecutions_MemoryTest"
        );

        Console.WriteLine(result.GetSummary());

        // Concurrent executions with shared cache should not cause memory leaks
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "Concurrent executions should not cause significant memory retention (< 10MB)"
            );
    }

    [Test]
    public async Task TrainBus_PerformanceComparison_CachedVsUncached()
    {
        // This test simulates the performance difference between cached and uncached lookups
        // Since we can't easily disable caching, we compare cold starts vs warm executions

        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act - Cold start (first execution - cache miss)
        var coldStartStopwatch = Stopwatch.StartNew();
        var coldInput = MemoryTestModelFactory.CreateInput("cold_start");
        await trainBus.RunAsync<MemoryTestOutput>(coldInput);
        coldStartStopwatch.Stop();

        // Warm executions (cache hits)
        var warmStopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            var warmInput = MemoryTestModelFactory.CreateInput($"warm_{i}");
            await trainBus.RunAsync<MemoryTestOutput>(warmInput);
        }

        warmStopwatch.Stop();

        // Assert
        var coldStartMs = coldStartStopwatch.ElapsedMilliseconds;
        var warmAverageMs = warmStopwatch.ElapsedMilliseconds / 10.0;

        Console.WriteLine($"Cold start time: {coldStartMs}ms");
        Console.WriteLine($"Warm execution average: {warmAverageMs:F2}ms");

        // Floored, because the bound is otherwise set by however fast the single cold run happened
        // to be: a cold start that lands at 1ms demands warm runs under 1.2ms, which says nothing
        // about caching and fails whenever the runner is busy. The floor keeps the real claim — a
        // warm run is never slow in absolute terms — while a genuine cache regression still trips it.
        warmAverageMs
            .Should()
            .BeLessThanOrEqualTo(
                Math.Max(coldStartMs * 1.2, ComparableExecutionFloorMs),
                "Cached executions should be reasonably fast compared to cold start"
            );
    }

    [Test]
    public async Task TrainBus_ShouldMaintainCacheEfficiency_AcrossServiceScopes()
    {
        // Test that the static cache works across different service provider scopes

        var results = new List<(string scope, double averageMs)>();

        for (int scope = 0; scope < 3; scope++)
        {
            using var scopedProvider = (IDisposable)TestSetup.CreateMemoryOnlyTestServiceProvider();
            var serviceProvider = (IServiceProvider)scopedProvider;
            var trainBus = serviceProvider.GetRequiredService<ITrainBus>();

            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 20; i++)
            {
                var testInput = MemoryTestModelFactory.CreateInput($"scope_{scope}_item_{i}");
                var result = await trainBus.RunAsync<MemoryTestOutput>(testInput);
                result.Should().NotBeNull();
            }

            stopwatch.Stop();
            var averageMs = stopwatch.ElapsedMilliseconds / 20.0;
            results.Add(($"Scope {scope}", averageMs));

            Console.WriteLine($"Scope {scope} average execution time: {averageMs:F2}ms");
        }

        // Assert - Later scopes should benefit from the shared static cache
        var firstScopeAverage = results[0].averageMs;
        var laterScopesAverage = results.Skip(1).Average(r => r.averageMs);

        // Floored for the same reason as the cold-start comparison above.
        laterScopesAverage
            .Should()
            .BeLessThanOrEqualTo(
                Math.Max(firstScopeAverage * 1.5, ComparableExecutionFloorMs),
                "Later scopes should benefit from shared static reflection cache"
            );
    }
}
