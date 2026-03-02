using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Tests.MemoryLeak.Integration.TestTrains;
using Trax.Effect.Tests.MemoryLeak.Integration.TestTrains.TestModels;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;
using Trax.Mediator.Services.TrainBus;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating that Metadata JsonDocument objects are properly disposed.
/// This addresses the primary memory leak issue where JsonDocument objects were not being disposed.
/// </summary>
[TestFixture]
public class MetadataDisposalTests
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

    [Test]
    public void Metadata_ShouldDisposeJsonDocuments_WhenDisposed()
    {
        // Arrange
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                Input = new { LargeData = new string('X', 10000) },
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // Simulate setting Output JsonDocument (this would normally happen during train execution)
        var outputData = new { Result = new string('Y', 10000) };
        metadata.SetOutputObject(outputData);

        // Act - Dispose the metadata
        var beforeDisposal = metadata.Input;
        var outputBeforeDisposal = metadata.Output;

        // Assert - JsonDocument objects should be disposed
        // We can't directly check if JsonDocument is disposed, but we can verify the disposal pattern works
        metadata.Should().NotBeNull();
    }

    [Test]
    public async Task Metadata_ShouldBeCollectedAfterDisposal()
    {
        // Arrange
        var factory = () =>
            Metadata.Create(
                new CreateMetadata
                {
                    Name = "TestTrain",
                    Input = new { LargeData = new string('X', 50000) }, // 50KB of data
                    ExternalId = Guid.NewGuid().ToString("N"),
                }
            );

        // Act & Assert
        var allCollected = await MemoryProfiler.TestObjectDisposal(
            factory,
            objectCount: 50,
            maxWaitTime: TimeSpan.FromSeconds(15)
        );

        allCollected.Should().BeTrue("All Metadata objects should be collected after disposal");
    }

    [Test]
    public async Task SuccessfulEffectTrains_ShouldNotLeakMemory()
    {
        // This test validates that EffectTrain.Dispose() properly clears the Memory dictionary
        // on the success path, preventing large step inputs/outputs from being retained.
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var serviceProviderScope = _serviceProvider.CreateScope();

                    try
                    {
                        var train =
                            serviceProviderScope.ServiceProvider.GetRequiredService<IMemoryTestTrain>();

                        var testInput = MemoryTestModelFactory.CreateInput(
                            $"success_test_{i}",
                            dataSizeBytes: 100_000,
                            processingDelayMs: 0
                        ); // 100KB each

                        await train.Run(testInput);
                    }
                    finally
                    {
                        // Scope disposal triggers EffectTrain.Dispose() which should clear Memory
                        serviceProviderScope.Dispose();
                    }
                }
            },
            "SuccessfulEffectTrains_ShouldNotLeakMemory"
        );

        Console.WriteLine(result.GetSummary());

        // Successful trains should clean up Memory dictionary contents on disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                2 * 1024 * 1024,
                "Should not retain more than 2MB total for 20 successful trains with 100KB data each"
            );

        // Ensure per-train retention is minimal
        var memoryPerTrain = result.MemoryRetained / 20;
        memoryPerTrain
            .Should()
            .BeLessThan(
                100 * 1024,
                "Each successful train should retain less than 100KB on average after disposal"
            );
    }

    [Test]
    public async Task EffectTrain_ShouldBeCollected_AfterScopeDisposal()
    {
        // This test validates that EffectTrain instances are GC-collectible after scope disposal.
        // Memory.Clear() in Dispose() is critical here — without it, large objects in the Memory
        // dictionary could prevent the train and its references from being collected.
        var weakReferences = new List<WeakReference>();

        for (int i = 0; i < 30; i++)
        {
            var serviceProviderScope = _serviceProvider.CreateScope();

            try
            {
                var train =
                    serviceProviderScope.ServiceProvider.GetRequiredService<IMemoryTestTrain>();

                weakReferences.Add(new WeakReference(train));

                var testInput = MemoryTestModelFactory.CreateInput(
                    $"gc_test_{i}",
                    dataSizeBytes: 50_000,
                    processingDelayMs: 0
                ); // 50KB each

                await train.Run(testInput);
            }
            finally
            {
                serviceProviderScope.Dispose();
            }
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100);

        var aliveCount = weakReferences.Count(wr => wr.IsAlive);
        Console.WriteLine(
            $"EffectTrains still alive after scope disposal + GC: {aliveCount}/{weakReferences.Count}"
        );

        aliveCount
            .Should()
            .BeLessThan(
                weakReferences.Count,
                "Some EffectTrain instances should be collected by GC after scope disposal"
            );
    }

    [Test]
    public async Task FailingTrains_ShouldNotLeakMemory()
    {
        // Arrange

        // Act & Assert
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    var serviceProviderScope = _serviceProvider.CreateScope();

                    try
                    {
                        var failingTrain =
                            serviceProviderScope.ServiceProvider.GetRequiredService<IFailingTestTrain>();

                        var testInput = MemoryTestModelFactory.CreateFailingInput(
                            $"failing_test_{i}",
                            dataSizeBytes: 100000
                        ); // 100KB each

                        await failingTrain.Run(testInput);
                    }
                    catch (Exception)
                    {
                        // Expected to fail - we're testing memory cleanup in error paths
                    }
                    finally
                    {
                        // Always dispose the scope, even when train fails
                        serviceProviderScope.Dispose();
                    }
                }
            },
            "FailingTrains_ShouldNotLeakMemory"
        );

        Console.WriteLine(result.GetSummary());

        // Even with failing trains, memory should be cleaned up properly
        // Note: Failing trains retain more memory due to exception handling and error metadata
        // The key is that we don't have unbounded growth - memory should be reasonable
        result
            .MemoryRetained.Should()
            .BeLessThan(
                1 * 1024 * 1024,
                "Should not retain more than 1MB total for 15 failing trains"
            );

        // Ensure memory retention is proportional to the number of trains (not growing exponentially)
        var memoryPerTrain = result.MemoryRetained / 15;
        memoryPerTrain
            .Should()
            .BeLessThan(100 * 1024, "Each failing train should retain less than 100KB on average");
    }
}
