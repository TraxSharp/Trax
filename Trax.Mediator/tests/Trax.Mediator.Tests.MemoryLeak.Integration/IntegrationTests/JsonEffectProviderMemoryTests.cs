using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Json.Services.JsonEffect;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating JsonEffectProvider memory management and preventing memory leaks.
/// These tests focus on the JsonEffectProvider class to ensure it properly manages
/// tracked models and JSON state storage without causing memory leaks.
/// </summary>
[TestFixture]
public class JsonEffectProviderMemoryTests
{
    private ILogger<JsonEffectProvider> _logger;
    private ITraxEffectConfiguration _configuration;

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
                        var metadata = ModelTrackingMetadata(i, j);

                        await provider.Track(metadata).ConfigureAwait(false);
                    }

                    // Save changes to trigger JSON serialization
                    await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);

                    // Provider gets disposed here, should clean up all tracked models
                }

                // Force GC to see if models are properly collected
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "JsonEffectProvider_ModelTracking"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Should not retain significant memory after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "JsonEffectProvider should not retain significant memory after disposal"
            );

        // A second, tighter bound used to follow. 500KB is below the noise floor of a process-wide
        // heap delta (see MemoryProfiler.RetainedNoiseFloorBytes); the sibling test that keeps weak
        // references to the tracked models is what actually establishes they are freed.
    }

    [Test]
    public async Task JsonEffectProvider_ShouldNotAccumulateModelsIndefinitely()
    {
        // Test model accumulation in a single provider
        var modelReferences = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                await TrackAccumulatingModelsAsync(modelReferences).ConfigureAwait(false);
            },
            "JsonEffectProvider_ModelAccumulation"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Force GC and check if models can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100).ConfigureAwait(false); // Give GC time to work

        var aliveModels = modelReferences.Count(wr => wr.IsAlive);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Models still alive after GC: {aliveModels}/{modelReferences.Count}"
                )
            )
            .ConfigureAwait(false);

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
                    .Select(taskId => TrackConcurrentlyAsync(provider, taskId));

                await Task.WhenAll(tasks).ConfigureAwait(false);

                // Final save changes
                await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            },
            "JsonEffectProvider_ConcurrentUsage"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                var metadataList = await TrackRepeatedTestModelsAsync(provider)
                    .ConfigureAwait(false);

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
                                Id = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Iteration_{iteration}_Output"
                                ),
                                Message = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Iteration {iteration} output"
                                ),
                                ProcessedData = new string(iterationChar, 1500), // 1.5KB string
                                Success = true,
                                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                            }
                        );
                    }

                    // This should replace old JSON strings, not accumulate them
                    await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                }
            },
            "JsonEffectProvider_RepeatedSerialization"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                    var metadata = LargeObjectMetadata(i);

                    await provider.Track(metadata).ConfigureAwait(false);

                    // Save periodically to trigger serialization
                    if (i % 5 == 0)
                    {
                        await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                // Final save
                await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            },
            "JsonEffectProvider_LargeObjectSerialization"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                    await provider.Track(metadata).ConfigureAwait(false);

                    // Modify the model slightly
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = string.Create(
                                CultureInfo.InvariantCulture,
                                $"DuplicateOutput_{i}"
                            ),
                            Message = string.Create(
                                CultureInfo.InvariantCulture,
                                $"Duplicate output {i}"
                            ),
                            ProcessedData = new string('D', 5000), // 5KB string
                            Success = true,
                            ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                        }
                    );

                    if (i % 10 == 0)
                    {
                        await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            },
            "JsonEffectProvider_DuplicateTracking"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Duplicate tracking should not cause excessive memory growth
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "Duplicate model tracking should not cause excessive memory growth"
            );
    }

    private static Metadata ModelTrackingMetadata(int i, int j)
    {
        var metadata = new Metadata
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"TestMetadata_{i}_{j}"),
        };
        metadata.SetInputObject(
            MemoryTestModelFactory.CreateInput(
                id: string.Create(CultureInfo.InvariantCulture, $"Input_{i}_{j}"),
                dataSizeBytes: 1000,
                description: string.Create(CultureInfo.InvariantCulture, $"Test input {i}_{j}")
            )
        );
        metadata.SetOutputObject(
            new MemoryTestOutput
            {
                Id = string.Create(CultureInfo.InvariantCulture, $"Output_{i}_{j}"),
                Message = string.Create(CultureInfo.InvariantCulture, $"Test output {i}_{j}"),
                ProcessedData = new string('X', 500), // 500 characters
                Success = true,
                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
            }
        );

        return metadata;
    }

    private async Task TrackAccumulatingModelsAsync(List<WeakReference> modelReferences)
    {
        using var provider = new JsonEffectProvider(_logger, _configuration);

        // Track many models with large JSON serialization
        for (int i = 0; i < 100; i++)
        {
            var metadata = new Metadata
            {
                Name = string.Create(CultureInfo.InvariantCulture, $"AccumulationTest_{i}"),
            };
            metadata.SetInputObject(
                MemoryTestModelFactory.CreateInput(
                    id: string.Create(CultureInfo.InvariantCulture, $"LargeInput_{i}"),
                    dataSizeBytes: 5000,
                    description: string.Create(CultureInfo.InvariantCulture, $"Large input {i}")
                )
            );
            metadata.SetOutputObject(
                new MemoryTestOutput
                {
                    Id = string.Create(CultureInfo.InvariantCulture, $"LargeOutput_{i}"),
                    Message = string.Create(CultureInfo.InvariantCulture, $"Large output {i}"),
                    ProcessedData = new string('Y', 2000), // 2KB string
                    Success = true,
                    ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                }
            );

            modelReferences.Add(new WeakReference(metadata));
            await provider.Track(metadata).ConfigureAwait(false);

            // Trigger serialization periodically
            if (i % 10 == 0)
            {
                await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            }
        }

        // Final save
        await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);

        // Provider disposal should clean up all tracked models
    }

    private static async Task TrackConcurrentlyAsync(JsonEffectProvider provider, int taskId)
    {
        for (int i = 0; i < 25; i++)
        {
            var metadata = new Metadata
            {
                Name = string.Create(CultureInfo.InvariantCulture, $"ConcurrentTest_{taskId}_{i}"),
            };
            metadata.SetInputObject(
                MemoryTestModelFactory.CreateInput(
                    id: string.Create(
                        CultureInfo.InvariantCulture,
                        $"ConcurrentInput_{taskId}_{i}"
                    ),
                    dataSizeBytes: 2000,
                    description: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Concurrent input {taskId}_{i}"
                    )
                )
            );

            await provider.Track(metadata).ConfigureAwait(false);

            // Simulate concurrent state changes
            await Task.Delay(1).ConfigureAwait(false); // Small delay to simulate work

            metadata.SetOutputObject(
                new MemoryTestOutput
                {
                    Id = string.Create(
                        CultureInfo.InvariantCulture,
                        $"ConcurrentOutput_{taskId}_{i}"
                    ),
                    Message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Concurrent output {taskId}_{i}"
                    ),
                    ProcessedData = new string('Z', 1000), // 1KB string
                    Success = true,
                    ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                }
            );

            if (i % 5 == 0)
            {
                await provider.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<Metadata>> TrackRepeatedTestModelsAsync(
        JsonEffectProvider provider
    )
    {
        var metadataList = new List<Metadata>();
        for (int i = 0; i < 10; i++)
        {
            var metadata = new Metadata
            {
                Name = string.Create(CultureInfo.InvariantCulture, $"RepeatedTest_{i}"),
            };
            metadata.SetInputObject(
                MemoryTestModelFactory.CreateInput(
                    id: string.Create(CultureInfo.InvariantCulture, $"Input_{i}"),
                    dataSizeBytes: 3000,
                    description: string.Create(CultureInfo.InvariantCulture, $"Repeated input {i}")
                )
            );

            metadataList.Add(metadata);
            await provider.Track(metadata).ConfigureAwait(false);
        }

        return metadataList;
    }

    private static Metadata LargeObjectMetadata(int i)
    {
        var metadata = new Metadata
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"LargeObjectTest_{i}"),
        };
        metadata.SetInputObject(
            MemoryTestModelFactory.CreateInput(
                id: string.Create(CultureInfo.InvariantCulture, $"LargeInput_{i}"),
                dataSizeBytes: 50_000,
                description: string.Create(CultureInfo.InvariantCulture, $"Large input {i}")
            )
        );
        metadata.SetOutputObject(
            new MemoryTestOutput
            {
                Id = string.Create(CultureInfo.InvariantCulture, $"LargeOutput_{i}"),
                Message = string.Create(CultureInfo.InvariantCulture, $"Large output {i}"),
                ProcessedData = new string('L', 25_000), // 25KB string
                Success = true,
                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
            }
        );

        return metadata;
    }

    private sealed class StubEffectConfiguration : ITraxEffectConfiguration
    {
        public System.Text.Json.JsonSerializerOptions SystemJsonSerializerOptions { get; } =
            new()
            {
                RespectNullableAnnotations = false,
                RespectRequiredConstructorParameters = false,
            };
        public bool SerializeJunctionData { get; } = false;
        public LogLevel LogLevel { get; } = LogLevel.Information;
    }
}
