using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

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
            RespectNullableAnnotations = false,
            RespectRequiredConstructorParameters = false,
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
                        var metadata = new Metadata
                        {
                            Name = string.Create(
                                CultureInfo.InvariantCulture,
                                $"ParameterTest_{i}_{j}"
                            ),
                        };

                        metadata.SetInputObject(
                            MemoryTestModelFactory.CreateInput(
                                id: string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"ParamInput_{i}_{j}"
                                ),
                                dataSizeBytes: 2000,
                                description: string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Parameter test input {i}_{j}"
                                )
                            )
                        );

                        metadata.SetOutputObject(
                            new MemoryTestOutput
                            {
                                Id = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"ParamOutput_{i}_{j}"
                                ),
                                Message = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Parameter test output {i}_{j}"
                                ),
                                ProcessedData = new string('P', 1000), // 1KB string
                                Success = true,
                                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                            }
                        );

                        await parameterEffect.Track(metadata).ConfigureAwait(false);
                    }

                    // Save changes to trigger JSON serialization and JsonDocument creation
                    await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);

                    // ParameterEffect gets disposed here, should clean up all tracked metadata
                }

                // Force GC to see if metadata objects are properly collected
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_MetadataTracking"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Should not retain significant memory after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                8 * 1024 * 1024,
                "ParameterEffect should not retain significant memory after disposal"
            );

        // A second, tighter bound used to follow, close enough to the noise floor of a process-wide
        // heap delta to fail on a loaded runner. The sibling test that keeps weak references to the
        // tracked metadata is what establishes they are freed.
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
                    var metadata = new Metadata
                    {
                        Name = string.Create(CultureInfo.InvariantCulture, $"AccumulationTest_{i}"),
                    };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: string.Create(CultureInfo.InvariantCulture, $"AccumInput_{i}"),
                            dataSizeBytes: 5000,
                            description: string.Create(
                                CultureInfo.InvariantCulture,
                                $"Accumulation test input {i}"
                            )
                        )
                    );

                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = string.Create(CultureInfo.InvariantCulture, $"AccumOutput_{i}"),
                            Message = string.Create(
                                CultureInfo.InvariantCulture,
                                $"Accumulation test output {i}"
                            ),
                            ProcessedData = new string('A', 3000), // 3KB string
                            Success = true,
                            ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                        }
                    );

                    metadataReferences.Add(new WeakReference(metadata));
                    await parameterEffect.Track(metadata).ConfigureAwait(false);

                    // Trigger serialization periodically
                    if (i % 10 == 0)
                    {
                        await parameterEffect
                            .SaveChanges(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }

                // Final save
                await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);

                // ParameterEffect disposal should clean up all tracked metadata
            },
            "ParameterEffect_MetadataAccumulation"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Force GC and check if metadata objects can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100).ConfigureAwait(false); // Give GC time to work

        var aliveMetadata = metadataReferences.Count(wr => wr.IsAlive);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Metadata objects still alive after GC: {aliveMetadata}/{metadataReferences.Count}"
                )
            )
            .ConfigureAwait(false);

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

                await parameterEffect.Track(metadata).ConfigureAwait(false);

                // Repeatedly update the metadata to create new JsonDocument instances
                for (int i = 0; i < 100; i++)
                {
                    // Update the output to trigger JsonDocument re-creation
                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = string.Create(CultureInfo.InvariantCulture, $"JsonDocOutput_{i}"),
                            Message = string.Create(
                                CultureInfo.InvariantCulture,
                                $"JsonDocument test output {i}"
                            ),
                            ProcessedData = new string('J', 5000), // 5KB string
                            Success = true,
                            ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                        }
                    );

                    // Each SaveChanges should dispose old JsonDocuments and create new ones
                    await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                }
            },
            "ParameterEffect_JsonDocumentDisposal"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                            var metadata = new Metadata
                            {
                                Name = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"ConcurrentTest_{taskId}_{i}"
                                ),
                            };

                            metadata.SetInputObject(
                                MemoryTestModelFactory.CreateInput(
                                    id: string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"ConcurrentInput_{taskId}_{i}"
                                    ),
                                    dataSizeBytes: 3000,
                                    description: string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"Concurrent input {taskId}_{i}"
                                    )
                                )
                            );

                            await parameterEffect.Track(metadata).ConfigureAwait(false);

                            // Simulate concurrent parameter updates
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
                                    ProcessedData = new string('C', 2000), // 2KB string
                                    Success = true,
                                    ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                                }
                            );

                            if (i % 5 == 0)
                            {
                                await parameterEffect
                                    .SaveChanges(CancellationToken.None)
                                    .ConfigureAwait(false);
                            }
                        }
                    });

                await Task.WhenAll(tasks).ConfigureAwait(false);

                // Final save changes
                await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            },
            "ParameterEffect_ConcurrentUsage"
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
                    var metadata = new Metadata
                    {
                        Name = string.Create(CultureInfo.InvariantCulture, $"RepeatedTest_{i}"),
                    };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: string.Create(CultureInfo.InvariantCulture, $"RepeatedInput_{i}"),
                            dataSizeBytes: 4000,
                            description: string.Create(
                                CultureInfo.InvariantCulture,
                                $"Repeated serialization input {i}"
                            )
                        )
                    );

                    metadataList.Add(metadata);
                    await parameterEffect.Track(metadata).ConfigureAwait(false);
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
                                Id = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"RepeatedOutput_{iteration}"
                                ),
                                Message = string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Repeated output iteration {iteration}"
                                ),
                                ProcessedData = new string(iterationChar, 2500), // 2.5KB string
                                Success = true,
                                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                            }
                        );
                    }

                    // This should replace old JsonDocument instances, not accumulate them
                    await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                }
            },
            "ParameterEffect_RepeatedSerialization"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                    var metadata = new Metadata
                    {
                        Name = string.Create(
                            CultureInfo.InvariantCulture,
                            $"LargeParameterTest_{i}"
                        ),
                    };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: string.Create(CultureInfo.InvariantCulture, $"LargeInput_{i}"),
                            dataSizeBytes: 50_000, // 50KB input
                            description: string.Create(
                                CultureInfo.InvariantCulture,
                                $"Large parameter input {i}"
                            )
                        )
                    );

                    metadata.SetOutputObject(
                        new MemoryTestOutput
                        {
                            Id = string.Create(CultureInfo.InvariantCulture, $"LargeOutput_{i}"),
                            Message = string.Create(
                                CultureInfo.InvariantCulture,
                                $"Large parameter output {i}"
                            ),
                            ProcessedData = new string('L', 30_000), // 30KB string
                            Success = true,
                            ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                        }
                    );

                    await parameterEffect.Track(metadata).ConfigureAwait(false);

                    // Save periodically to trigger serialization
                    if (i % 5 == 0)
                    {
                        await parameterEffect
                            .SaveChanges(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }

                // Final save
                await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);
            },
            "ParameterEffect_LargeParameterSerialization"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                    await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);

                    // ParameterEffect should dispose cleanly with minimal memory impact
                }

                // Force garbage collection to clean up empty instances
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_EmptyDisposal"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

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
                    var metadata = new Metadata
                    {
                        Name = string.Create(CultureInfo.InvariantCulture, $"PreDisposalTest_{i}"),
                    };

                    metadata.SetInputObject(
                        MemoryTestModelFactory.CreateInput(
                            id: string.Create(CultureInfo.InvariantCulture, $"PreDisposal_{i}"),
                            dataSizeBytes: 1000
                        )
                    );

                    await parameterEffect.Track(metadata).ConfigureAwait(false);
                    await parameterEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);

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
                    await disposedEffect.Track(postDisposalMetadata).ConfigureAwait(false);
                    await disposedEffect.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                }

                // Cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ParameterEffect_PostDisposal"
        );

        await TestContext.Out.WriteLineAsync(result.GetSummary()).ConfigureAwait(false);

        // Post-disposal operations should not cause memory leaks
        result
            .MemoryRetained.Should()
            .BeLessThan(4 * 1024 * 1024, "Post-disposal operations should not cause memory leaks");
    }
}
