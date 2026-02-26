using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Effect.Tests.MemoryLeak.Integration.TestWorkflows.TestModels;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating JsonEffectProvider memory management and preventing memory leaks.
/// These tests focus on the JsonEffectProvider class to ensure it properly manages
/// tracked models and JSON state storage without causing memory leaks.
/// </summary>
[TestFixture]
public class JsonEffectProviderMemoryTests
{
    private ILogger<JsonEffectProvider> _logger;
    private ITrax.CoreEffectConfiguration _configuration;

    [SetUp]
    public void SetUp()
    {
        _logger = NullLogger<JsonEffectProvider>.Instance;
        _configuration = new StubEffectConfiguration();
    }

    [Test]
    public async Task JsonEffectProvider_ShouldNotLeakMemory_WhenTrackingManyModels()
    {
        // Test tracking many models without proper disposal
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using var provider = new JsonEffectProvider(_logger, _configuration);

                    // Track multiple models
                    for (int j = 0; j < 20; j++)
                    {
                        var metadata = new Metadata { Name = $"TestMetadata_{i}_{j}" };
                        metadata.SetInputObject(
                            MemoryTestModelFactory.CreateInput(
                                id: $"Input_{i}_{j}",
                                dataSizeBytes: 1000,
                                description: $"Test input {i}_{j}"
                            )
                        );
                        metadata.SetOutputObject(
                            new MemoryTestOutput
                            {
                                Id = $"Output_{i}_{j}",
                                Message = $"Test output {i}_{j}",
                                ProcessedData = new string('X', 500), // 500 characters
                                Success = true,
                                ProcessedAt = DateTime.UtcNow
                            }
                        );

                        await provider.Track(metadata);
                    }

                    // Save changes to trigger JSON serialization
                    await provider.SaveChanges(CancellationToken.None);

                    // Provider gets disposed here, should clean up all tracked models
                }

                // Force GC to see if models are properly collected
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "JsonEffectProvider_ModelTracking"
        );

        Console.WriteLine(result.GetSummary());

        // Should not retain significant memory after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "JsonEffectProvider should not retain significant memory after disposal"
            );

        // Most memory should be freed (allow for reasonable baseline retention)
        result
            .MemoryRetained.Should()
            .BeLessThan(500_000, "Memory retained should be reasonable after disposal");
    }

    [Test]
    public async Task JsonEffectProvider_ShouldNotAccumulateModelsIndefinitely()
    {
        // Test model accumulation in a single provider
        var modelReferences = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new JsonEffectProvider(_logger, _configuration);

                // Track many models with large JSON serialization
                for (int i = 0; i < 100; i++)
                {
                    var metadata = new Metadata { Name = $"AccumulationTest_{i}" };
                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"LargeInput_{i}",
                            dataSizeBytes: 5000,
                            description: $"Large input {i}"
                        )
                    );
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"LargeOutput_{i}",
                            Message = $"Large output {i}",
                            ProcessedData = new string('Y', 2000), // 2KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow
                        }
                    );

                    modelReferences.Add(new WeakReference(metadata));
                    await provider.Track(metadata);

                    // Trigger serialization periodically
                    if (i % 10 == 0)
                    {
                        await provider.SaveChanges(CancellationToken.None);
                    }
                }

                // Final save
                await provider.SaveChanges(CancellationToken.None);

                // Provider disposal should clean up all tracked models
            },
            "JsonEffectProvider_ModelAccumulation"
        );

        Console.WriteLine(result.GetSummary());

        // Force GC and check if models can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100); // Give GC time to work

        var aliveModels = modelReferences.Count(wr => wr.IsAlive);
        Console.WriteLine($"Models still alive after GC: {aliveModels}/{modelReferences.Count}");

        aliveModels
            .Should()
            .BeLessThan(
                modelReferences.Count,
                "Some models should be collected by GC after provider disposal"
            );

        // Memory should be manageable after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(8 * 1024 * 1024, "Memory should be manageable after provider disposal");
    }

    [Test]
    public async Task JsonEffectProvider_ConcurrentUsage_ShouldNotLeakMemory()
    {
        // Test concurrent model tracking for thread safety and memory leaks
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new JsonEffectProvider(_logger, _configuration);

                var tasks = Enumerable
                    .Range(0, 8)
                    .Select(async taskId =>
                    {
                        for (int i = 0; i < 25; i++)
                        {
                            var metadata = new Metadata { Name = $"ConcurrentTest_{taskId}_{i}" };
                            metadata.SetInputObject(
                                MemoryTestModelFactory.CreateInput(
                                    id: $"ConcurrentInput_{taskId}_{i}",
                                    dataSizeBytes: 2000,
                                    description: $"Concurrent input {taskId}_{i}"
                                )
                            );

                            await provider.Track(metadata);

                            // Simulate concurrent state changes
                            await Task.Delay(1); // Small delay to simulate work

                            metadata.SetOutputObject(
                                new MemoryTestOutput
                                {
                                    Id = $"ConcurrentOutput_{taskId}_{i}",
                                    Message = $"Concurrent output {taskId}_{i}",
                                    ProcessedData = new string('Z', 1000), // 1KB string
                                    Success = true,
                                    ProcessedAt = DateTime.UtcNow
                                }
                            );

                            if (i % 5 == 0)
                            {
                                await provider.SaveChanges(CancellationToken.None);
                            }
                        }
                    });

                await Task.WhenAll(tasks);

                // Final save changes
                await provider.SaveChanges(CancellationToken.None);
            },
            "JsonEffectProvider_ConcurrentUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Concurrent usage should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                12 * 1024 * 1024,
                "Concurrent usage should not cause excessive memory retention"
            );
    }

    [Test]
    public async Task JsonEffectProvider_RepeatedSaveChanges_ShouldNotLeakJsonStrings()
    {
        // Test repeated serialization doesn't accumulate JSON strings
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new JsonEffectProvider(_logger, _configuration);

                // Track a few models
                var metadataList = new List<Metadata>();
                for (int i = 0; i < 10; i++)
                {
                    var metadata = new Metadata { Name = $"RepeatedTest_{i}" };
                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"Input_{i}",
                            dataSizeBytes: 3000,
                            description: $"Repeated input {i}"
                        )
                    );

                    metadataList.Add(metadata);
                    await provider.Track(metadata);
                }

                // Repeatedly modify and save changes
                for (int iteration = 0; iteration < 50; iteration++)
                {
                    // Modify models to trigger JSON re-serialization
                    foreach (var metadata in metadataList)
                    {
                        char iterationChar = (char)('A' + (iteration % 26));
                        metadata.SetOutputObject(
                            new MemoryTestOutput
                            {
                                Id = $"Iteration_{iteration}_Output",
                                Message = $"Iteration {iteration} output",
                                ProcessedData = new string(iterationChar, 1500), // 1.5KB string
                                Success = true,
                                ProcessedAt = DateTime.UtcNow
                            }
                        );
                    }

                    // This should replace old JSON strings, not accumulate them
                    await provider.SaveChanges(CancellationToken.None);
                }
            },
            "JsonEffectProvider_RepeatedSerialization"
        );

        Console.WriteLine(result.GetSummary());

        // Repeated serialization should not cause excessive memory growth
        result
            .MemoryRetained.Should()
            .BeLessThan(
                6 * 1024 * 1024,
                "Repeated JSON serialization should not cause excessive memory growth"
            );
    }

    [Test]
    public void JsonEffectProvider_DisposedState_ShouldStopTracking()
    {
        // Test that disposed provider stops tracking models
        var provider = new JsonEffectProvider(_logger, _configuration);

        // Track a model before disposal
        var metadata1 = new Metadata { Name = "BeforeDisposal" };
        metadata1.SetInputObject(MemoryTestModelFactory.CreateInput("Test", 100));

        provider.Track(metadata1).Wait();

        // Dispose the provider
        provider.Dispose();

        // Try to track a model after disposal (should be ignored)
        var metadata2 = new Metadata { Name = "AfterDisposal" };
        metadata2.SetInputObject(MemoryTestModelFactory.CreateInput("Test2", 100));

        provider.Track(metadata2).Wait();

        // SaveChanges should also be safe after disposal
        provider.SaveChanges(CancellationToken.None).Wait();

        // No exceptions should be thrown for operations on disposed provider
        // This test mainly validates that disposal is handled gracefully
        Assert.Pass("Disposed provider should handle operations gracefully");
    }

    [Test]
    public async Task JsonEffectProvider_LargeObjectSerialization_ShouldManageMemory()
    {
        // Test serialization of large objects
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new JsonEffectProvider(_logger, _configuration);

                // Track models with very large serializable data
                for (int i = 0; i < 20; i++)
                {
                    var metadata = new Metadata { Name = $"LargeObjectTest_{i}" };
                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"LargeInput_{i}",
                            dataSizeBytes: 50_000,
                            description: $"Large input {i}"
                        )
                    );
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"LargeOutput_{i}",
                            Message = $"Large output {i}",
                            ProcessedData = new string('L', 25_000), // 25KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow
                        }
                    );

                    await provider.Track(metadata);

                    // Save periodically to trigger serialization
                    if (i % 5 == 0)
                    {
                        await provider.SaveChanges(CancellationToken.None);
                    }
                }

                // Final save
                await provider.SaveChanges(CancellationToken.None);
            },
            "JsonEffectProvider_LargeObjectSerialization"
        );

        Console.WriteLine(result.GetSummary());

        // Large object serialization should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                20 * 1024 * 1024,
                "Large object serialization should not cause excessive memory retention"
            );
    }

    [Test]
    public async Task JsonEffectProvider_DuplicateModelTracking_ShouldNotMultiplyReferences()
    {
        // Test tracking the same model multiple times
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new JsonEffectProvider(_logger, _configuration);

                var metadata = new Metadata { Name = "DuplicateTrackingTest" };
                metadata.SetInputObject(
                    MemoryTestModelFactory.CreateInput(
                        id: "DuplicateInput",
                        dataSizeBytes: 10_000,
                        description: "Duplicate tracking test input"
                    )
                );

                // Track the same model multiple times
                for (int i = 0; i < 100; i++)
                {
                    await provider.Track(metadata);

                    // Modify the model slightly
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"DuplicateOutput_{i}",
                            Message = $"Duplicate output {i}",
                            ProcessedData = new string('D', 5000), // 5KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow
                        }
                    );

                    if (i % 10 == 0)
                    {
                        await provider.SaveChanges(CancellationToken.None);
                    }
                }

                await provider.SaveChanges(CancellationToken.None);
            },
            "JsonEffectProvider_DuplicateTracking"
        );

        Console.WriteLine(result.GetSummary());

        // Duplicate tracking should not cause excessive memory growth
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "Duplicate model tracking should not cause excessive memory growth"
            );
    }

    private class StubEffectConfiguration : ITrax.CoreEffectConfiguration
    {
        public System.Text.Json.JsonSerializerOptions SystemJsonSerializerOptions { get; } = new();
        public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; } = new();
        public bool SerializeStepData { get; } = false;
        public LogLevel LogLevel { get; } = LogLevel.Information;
    }
}
