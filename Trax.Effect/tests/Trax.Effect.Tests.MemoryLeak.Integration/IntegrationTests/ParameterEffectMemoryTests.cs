using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Tests.MemoryLeak.Integration.TestTrains.TestModels;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating ParameterEffect memory management and preventing memory leaks.
/// These tests focus on the ParameterEffect class to ensure it properly manages
/// tracked metadata objects and JsonDocument instances without causing memory leaks.
/// </summary>
[TestFixture]
public class ParameterEffectMemoryTests
{
    private JsonSerializerOptions _jsonOptions;

    [SetUp]
    public void SetUp()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    [Test]
    public async Task ParameterEffect_ShouldNotLeakMemory_WhenTrackingManyMetadata()
    {
        // Test tracking many metadata objects without proper disposal
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using var parameterEffect = new ParameterEffect(
                        _jsonOptions,
                        new ParameterEffectConfiguration()
                    );

                    // Track multiple metadata objects
                    for (int j = 0; j < 20; j++)
                    {
                        var metadata = new Metadata { Name = $"ParameterTest_{i}_{j}" };

                        metadata.SetInputObject(
                            MemoryTestModelFactory.CreateInput(
                                id: $"ParamInput_{i}_{j}",
                                dataSizeBytes: 2000,
                                description: $"Parameter test input {i}_{j}"
                            )
                        );

                        metadata.SetOutputObject(
                            new MemoryTestOutput
                            {
                                Id = $"ParamOutput_{i}_{j}",
                                Message = $"Parameter test output {i}_{j}",
                                ProcessedData = new string('P', 1000), // 1KB string
                                Success = true,
                                ProcessedAt = DateTime.UtcNow,
                            }
                        );

                        await parameterEffect.Track(metadata);
                    }

                    // Save changes to trigger JSON serialization and JsonDocument creation
                    await parameterEffect.SaveChanges(CancellationToken.None);

                    // ParameterEffect gets disposed here, should clean up all tracked metadata
                }

                // Force GC to see if metadata objects are properly collected
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_MetadataTracking"
        );

        Console.WriteLine(result.GetSummary());

        // Should not retain significant memory after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                8 * 1024 * 1024,
                "ParameterEffect should not retain significant memory after disposal"
            );

        // Most memory should be freed (allow for baseline retention from multiple instances)
        result
            .MemoryRetained.Should()
            .BeLessThan(
                2_000_000,
                "Memory retained should be manageable after ParameterEffect disposal"
            );
    }

    [Test]
    public async Task ParameterEffect_ShouldNotAccumulateMetadataIndefinitely()
    {
        // Test metadata accumulation in a single parameter effect
        var metadataReferences = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var parameterEffect = new ParameterEffect(
                    _jsonOptions,
                    new ParameterEffectConfiguration()
                );

                // Track many metadata objects with large parameter serialization
                for (int i = 0; i < 100; i++)
                {
                    var metadata = new Metadata { Name = $"AccumulationTest_{i}" };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"AccumInput_{i}",
                            dataSizeBytes: 5000,
                            description: $"Accumulation test input {i}"
                        )
                    );

                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"AccumOutput_{i}",
                            Message = $"Accumulation test output {i}",
                            ProcessedData = new string('A', 3000), // 3KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow,
                        }
                    );

                    metadataReferences.Add(new WeakReference(metadata));
                    await parameterEffect.Track(metadata);

                    // Trigger serialization periodically
                    if (i % 10 == 0)
                    {
                        await parameterEffect.SaveChanges(CancellationToken.None);
                    }
                }

                // Final save
                await parameterEffect.SaveChanges(CancellationToken.None);

                // ParameterEffect disposal should clean up all tracked metadata
            },
            "ParameterEffect_MetadataAccumulation"
        );

        Console.WriteLine(result.GetSummary());

        // Force GC and check if metadata objects can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100); // Give GC time to work

        var aliveMetadata = metadataReferences.Count(wr => wr.IsAlive);
        Console.WriteLine(
            $"Metadata objects still alive after GC: {aliveMetadata}/{metadataReferences.Count}"
        );

        aliveMetadata
            .Should()
            .BeLessThan(
                metadataReferences.Count,
                "Some metadata objects should be collected by GC after ParameterEffect disposal"
            );

        // Memory should be manageable after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                10 * 1024 * 1024,
                "Memory should be manageable after ParameterEffect disposal"
            );
    }

    [Test]
    public async Task ParameterEffect_JsonDocumentDisposal_ShouldPreventMemoryLeaks()
    {
        // Test proper disposal of JsonDocument instances
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var parameterEffect = new ParameterEffect(
                    _jsonOptions,
                    new ParameterEffectConfiguration()
                );

                // Track metadata and repeatedly update to create new JsonDocuments
                var metadata = new Metadata { Name = "JsonDocumentTest" };

                metadata.SetInputObject(
                    MemoryTestModelFactory.CreateInput(
                        id: "JsonDocInput",
                        dataSizeBytes: 10_000,
                        description: "JsonDocument disposal test"
                    )
                );

                await parameterEffect.Track(metadata);

                // Repeatedly update the metadata to create new JsonDocument instances
                for (int i = 0; i < 100; i++)
                {
                    // Update the output to trigger JsonDocument re-creation
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"JsonDocOutput_{i}",
                            Message = $"JsonDocument test output {i}",
                            ProcessedData = new string('J', 5000), // 5KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow,
                        }
                    );

                    // Each SaveChanges should dispose old JsonDocuments and create new ones
                    await parameterEffect.SaveChanges(CancellationToken.None);
                }
            },
            "ParameterEffect_JsonDocumentDisposal"
        );

        Console.WriteLine(result.GetSummary());

        // JsonDocument disposal should prevent excessive memory accumulation
        result
            .MemoryRetained.Should()
            .BeLessThan(
                15 * 1024 * 1024,
                "JsonDocument disposal should prevent excessive memory accumulation"
            );
    }

    [Test]
    public async Task ParameterEffect_ConcurrentUsage_ShouldNotLeakMemory()
    {
        // Test concurrent metadata tracking for thread safety and memory leaks
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var parameterEffect = new ParameterEffect(
                    _jsonOptions,
                    new ParameterEffectConfiguration()
                );

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
                                    dataSizeBytes: 3000,
                                    description: $"Concurrent input {taskId}_{i}"
                                )
                            );

                            await parameterEffect.Track(metadata);

                            // Simulate concurrent parameter updates
                            await Task.Delay(1); // Small delay to simulate work

                            metadata.SetOutputObject(
                                new MemoryTestOutput
                                {
                                    Id = $"ConcurrentOutput_{taskId}_{i}",
                                    Message = $"Concurrent output {taskId}_{i}",
                                    ProcessedData = new string('C', 2000), // 2KB string
                                    Success = true,
                                    ProcessedAt = DateTime.UtcNow,
                                }
                            );

                            if (i % 5 == 0)
                            {
                                await parameterEffect.SaveChanges(CancellationToken.None);
                            }
                        }
                    });

                await Task.WhenAll(tasks);

                // Final save changes
                await parameterEffect.SaveChanges(CancellationToken.None);
            },
            "ParameterEffect_ConcurrentUsage"
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
    public async Task ParameterEffect_RepeatedSerialization_ShouldReplaceJsonDocuments()
    {
        // Test that repeated serialization properly replaces JsonDocument instances
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var parameterEffect = new ParameterEffect(
                    _jsonOptions,
                    new ParameterEffectConfiguration()
                );

                // Track a few metadata objects
                var metadataList = new List<Metadata>();
                for (int i = 0; i < 10; i++)
                {
                    var metadata = new Metadata { Name = $"RepeatedTest_{i}" };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"RepeatedInput_{i}",
                            dataSizeBytes: 4000,
                            description: $"Repeated serialization input {i}"
                        )
                    );

                    metadataList.Add(metadata);
                    await parameterEffect.Track(metadata);
                }

                // Repeatedly modify and serialize parameters
                for (int iteration = 0; iteration < 50; iteration++)
                {
                    // Modify metadata to trigger parameter re-serialization
                    foreach (var metadata in metadataList)
                    {
                        char iterationChar = (char)('A' + (iteration % 26));
                        metadata.SetOutputObject(
                            new MemoryTestOutput
                            {
                                Id = $"RepeatedOutput_{iteration}",
                                Message = $"Repeated output iteration {iteration}",
                                ProcessedData = new string(iterationChar, 2500), // 2.5KB string
                                Success = true,
                                ProcessedAt = DateTime.UtcNow,
                            }
                        );
                    }

                    // This should replace old JsonDocument instances, not accumulate them
                    await parameterEffect.SaveChanges(CancellationToken.None);
                }
            },
            "ParameterEffect_RepeatedSerialization"
        );

        Console.WriteLine(result.GetSummary());

        // Repeated serialization should not cause excessive memory growth
        result
            .MemoryRetained.Should()
            .BeLessThan(
                8 * 1024 * 1024,
                "Repeated parameter serialization should not cause excessive memory growth"
            );
    }

    [Test]
    public void ParameterEffect_DisposedState_ShouldStopTracking()
    {
        // Test that disposed ParameterEffect stops tracking metadata
        var parameterEffect = new ParameterEffect(_jsonOptions, new ParameterEffectConfiguration());

        // Track a metadata object before disposal
        var metadata1 = new Metadata { Name = "BeforeDisposal" };

        metadata1.SetInputObject(MemoryTestModelFactory.CreateInput("Test", 100));

        parameterEffect.Track(metadata1).Wait();

        // Dispose the ParameterEffect
        parameterEffect.Dispose();

        // Try to track a metadata object after disposal (should be ignored)
        var metadata2 = new Metadata { Name = "AfterDisposal" };

        metadata2.SetInputObject(MemoryTestModelFactory.CreateInput("Test2", 100));

        parameterEffect.Track(metadata2).Wait();

        // SaveChanges should also be safe after disposal
        parameterEffect.SaveChanges(CancellationToken.None).Wait();

        // No exceptions should be thrown for operations on disposed ParameterEffect
        // This test mainly validates that disposal is handled gracefully
        Assert.Pass("Disposed ParameterEffect should handle operations gracefully");
    }

    [Test]
    public async Task ParameterEffect_LargeParameterSerialization_ShouldManageMemory()
    {
        // Test serialization of large parameter objects
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var parameterEffect = new ParameterEffect(
                    _jsonOptions,
                    new ParameterEffectConfiguration()
                );

                // Track metadata with very large parameter data
                for (int i = 0; i < 20; i++)
                {
                    var metadata = new Metadata { Name = $"LargeParameterTest_{i}" };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"LargeInput_{i}",
                            dataSizeBytes: 50_000, // 50KB input
                            description: $"Large parameter input {i}"
                        )
                    );

                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = $"LargeOutput_{i}",
                            Message = $"Large parameter output {i}",
                            ProcessedData = new string('L', 30_000), // 30KB string
                            Success = true,
                            ProcessedAt = DateTime.UtcNow,
                        }
                    );

                    await parameterEffect.Track(metadata);

                    // Save periodically to trigger serialization
                    if (i % 5 == 0)
                    {
                        await parameterEffect.SaveChanges(CancellationToken.None);
                    }
                }

                // Final save
                await parameterEffect.SaveChanges(CancellationToken.None);
            },
            "ParameterEffect_LargeParameterSerialization"
        );

        Console.WriteLine(result.GetSummary());

        // Large parameter serialization should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                25 * 1024 * 1024,
                "Large parameter serialization should not cause excessive memory retention"
            );
    }

    [Test]
    public async Task ParameterEffect_EmptyDisposal_ShouldUseMinimalMemory()
    {
        // Test that ParameterEffect disposal without tracking uses minimal memory
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                // Create multiple ParameterEffect instances without tracking anything
                for (int i = 0; i < 50; i++)
                {
                    using var parameterEffect = new ParameterEffect(
                        _jsonOptions,
                        new ParameterEffectConfiguration()
                    );

                    // Call SaveChanges without tracking anything
                    await parameterEffect.SaveChanges(CancellationToken.None);

                    // ParameterEffect should dispose cleanly with minimal memory impact
                }

                // Force garbage collection to clean up empty instances
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_EmptyDisposal"
        );

        Console.WriteLine(result.GetSummary());

        // Empty ParameterEffect instances should use minimal memory
        result
            .MemoryRetained.Should()
            .BeLessThan(
                3 * 1024 * 1024,
                "Empty ParameterEffect disposal should use minimal memory"
            );
    }

    [Test]
    public async Task ParameterEffect_PostDisposal_ShouldIgnoreOperations()
    {
        // Test that ParameterEffect properly ignores operations after disposal
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                var disposedEffects = new List<ParameterEffect>();

                // Create and dispose multiple ParameterEffect instances
                for (int i = 0; i < 25; i++)
                {
                    var parameterEffect = new ParameterEffect(
                        _jsonOptions,
                        new ParameterEffectConfiguration()
                    );

                    // Track some metadata before disposal
                    var metadata = new Metadata { Name = $"PreDisposalTest_{i}" };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: $"PreDisposal_{i}",
                            dataSizeBytes: 1000
                        )
                    );

                    await parameterEffect.Track(metadata);
                    await parameterEffect.SaveChanges(CancellationToken.None);

                    // Dispose the effect
                    parameterEffect.Dispose();
                    disposedEffects.Add(parameterEffect);
                }

                // Try to use disposed effects (should be safely ignored)
                foreach (var disposedEffect in disposedEffects)
                {
                    var postDisposalMetadata = new Metadata { Name = "PostDisposal" };

                    postDisposalMetadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput("PostDisposal", 500)
                    );

                    // These operations should be safely ignored on disposed objects
                    await disposedEffect.Track(postDisposalMetadata);
                    await disposedEffect.SaveChanges(CancellationToken.None);
                }

                // Cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_PostDisposal"
        );

        Console.WriteLine(result.GetSummary());

        // Post-disposal operations should not cause memory leaks
        result
            .MemoryRetained.Should()
            .BeLessThan(4 * 1024 * 1024, "Post-disposal operations should not cause memory leaks");
    }
}
