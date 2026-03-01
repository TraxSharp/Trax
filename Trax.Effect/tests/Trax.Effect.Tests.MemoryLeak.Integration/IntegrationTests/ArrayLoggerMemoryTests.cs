using FluentAssertions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for validating ArrayLogger memory management and preventing memory leaks.
/// These tests focus on the ArrayLoggingProvider and ArrayLoggerEffect classes
/// to ensure they properly manage memory in long-running applications.
/// </summary>
[TestFixture]
public class ArrayLoggerMemoryTests
{
    [Test]
    public async Task ArrayLoggingProvider_ShouldNotLeakMemory_WhenCreatingManyLoggers()
    {
        // Test creating many loggers without proper disposal
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using var provider = new ArrayLoggingProvider();

                    // Create multiple loggers
                    for (int j = 0; j < 10; j++)
                    {
                        var logger = provider.CreateLogger($"TestLogger_{i}_{j}");

                        // Generate some log entries
                        for (int k = 0; k < 20; k++)
                        {
                            logger.LogInformation("Test log message {Index}", k);
                        }
                    }

                    // Provider gets disposed here, should clean up all loggers
                }

                // Force GC to see if loggers are properly collected
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            },
            "ArrayLoggingProvider_LoggerCreation"
        );

        Console.WriteLine(result.GetSummary());

        // Should not retain significant memory after disposal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "ArrayLoggingProvider should not retain significant memory after disposal"
            );

        // Most memory should be freed (allow for some baseline overhead)
        result
            .MemoryRetained.Should()
            .BeLessThan(2000, "Memory retained should be minimal after disposal");
    }

    [Test]
    public async Task ArrayLoggerEffect_ShouldNotAccumulateLogsIndefinitely()
    {
        // Test log accumulation in a single logger
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new ArrayLoggingProvider();
                var logger = provider.CreateLogger("AccumulationTest");

                // Generate many log entries
                for (int i = 0; i < 5000; i++)
                {
                    logger.LogInformation(
                        "Log message {Index} with some data: {Data}",
                        i,
                        new string('X', 100)
                    ); // 100 characters each
                }

                // Verify logs are accumulated
                var arrayLogger = provider.Loggers.First();
                arrayLogger.Logs.Count.Should().Be(5000);

                // Clear logs to prevent memory leak
                provider.ClearAllLogs();

                // Verify logs are cleared
                arrayLogger.Logs.Count.Should().Be(0);
            },
            "ArrayLoggerEffect_LogAccumulation"
        );

        Console.WriteLine(result.GetSummary());

        // Memory should be manageable after clearing logs
        result
            .MemoryRetained.Should()
            .BeLessThan(
                2 * 1024 * 1024,
                "Memory should be manageable after clearing accumulated logs"
            );
    }

    [Test]
    public async Task ArrayLoggingProvider_TrimLogs_ShouldLimitMemoryGrowth()
    {
        // Test automatic log trimming functionality
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new ArrayLoggingProvider();
                var logger = provider.CreateLogger("TrimTest");

                // Generate many log entries
                for (int i = 0; i < 2000; i++)
                {
                    logger.LogInformation("Log message {Index}", i);

                    // Trim logs every 100 entries to keep only last 500
                    if (i % 100 == 0 && i > 0)
                    {
                        provider.TrimLoggers(500);
                    }
                }

                // Verify logs are trimmed (allow for some logs added after last trim)
                var arrayLogger = provider.Loggers.First();
                arrayLogger
                    .Logs.Count.Should()
                    .BeLessThanOrEqualTo(
                        600,
                        "Logs should be approximately trimmed to the specified limit"
                    );
            },
            "ArrayLoggingProvider_LogTrimming"
        );

        Console.WriteLine(result.GetSummary());

        // Trimming should keep memory usage bounded
        result
            .MemoryRetained.Should()
            .BeLessThan(3 * 1024 * 1024, "Log trimming should keep memory usage bounded");
    }

    [Test]
    public async Task ArrayLoggingProvider_ConcurrentUsage_ShouldNotLeakMemory()
    {
        // Test concurrent logger usage for thread safety and memory leaks
        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new ArrayLoggingProvider();

                var tasks = Enumerable
                    .Range(0, 10)
                    .Select(async taskId =>
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            var logger = provider.CreateLogger($"ConcurrentLogger_{taskId}_{i}");

                            // Generate logs concurrently
                            await Task.Run(() =>
                            {
                                for (int j = 0; j < 30; j++)
                                {
                                    logger.LogInformation(
                                        "Concurrent log {TaskId}-{Index}-{LogIndex}",
                                        taskId,
                                        i,
                                        j
                                    );
                                }
                            });
                        }
                    });

                await Task.WhenAll(tasks);

                // Periodically trim logs during concurrent access
                provider.TrimLoggers(1000);
            },
            "ArrayLoggingProvider_ConcurrentUsage"
        );

        Console.WriteLine(result.GetSummary());

        // Concurrent usage should not cause excessive memory retention
        result
            .MemoryRetained.Should()
            .BeLessThan(
                8 * 1024 * 1024,
                "Concurrent usage should not cause excessive memory retention"
            );
    }

    [Test]
    public async Task ArrayLoggerEffect_DisposalPattern_ShouldCleanupProperly()
    {
        // Test proper disposal of individual logger effects
        var loggerReferences = new List<WeakReference>();

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using var provider = new ArrayLoggingProvider();
                    var logger = provider.CreateLogger($"DisposalTest_{i}");

                    loggerReferences.Add(new WeakReference(logger));

                    // Generate logs
                    for (int j = 0; j < 100; j++)
                    {
                        logger.LogInformation("Disposal test log {Index}", j);
                    }

                    // Provider disposal should clean up loggers
                }
            },
            "ArrayLoggerEffect_DisposalPattern"
        );

        Console.WriteLine(result.GetSummary());

        // Force GC and check if loggers can be collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100); // Give GC time to work

        var aliveLoggers = loggerReferences.Count(wr => wr.IsAlive);
        Console.WriteLine(
            $"Logger effects still alive after GC: {aliveLoggers}/{loggerReferences.Count}"
        );

        aliveLoggers
            .Should()
            .BeLessThan(
                loggerReferences.Count,
                "Some logger effects should be collected by GC after disposal"
            );

        // Memory retention should be minimal
        result
            .MemoryRetained.Should()
            .BeLessThan(
                result.MemoryAllocated / 3,
                "Most memory should be freed with proper disposal"
            );
    }

    [Test]
    public void ArrayLoggingProvider_DisposedState_ShouldThrowOnNewLoggers()
    {
        // Test that disposed provider throws exceptions appropriately
        var provider = new ArrayLoggingProvider();
        provider.Dispose();

        // Should throw when trying to create logger after disposal
        Action createLogger = () => provider.CreateLogger("TestLogger");
        createLogger.Should().Throw<ObjectDisposedException>();

        // Operations on disposed provider should be safe
        provider.ClearAllLogs(); // Should not throw
        provider.TrimLoggers(100); // Should not throw
    }

    [Test]
    public void ArrayLoggerEffect_DisposedState_ShouldStopLogging()
    {
        // Test that disposed logger effect stops accepting logs
        using var provider = new ArrayLoggingProvider();
        var logger = provider.CreateLogger("DisposedTest");

        // Log before disposal
        logger.LogInformation("Before {Action}", "disposal");

        var arrayLogger = provider.Loggers.First();
        arrayLogger.Logs.Count.Should().Be(1);

        // Dispose the logger
        arrayLogger.Dispose();

        // Log after disposal
        logger.LogInformation("After {Action}", "disposal");

        // Should still have only 1 log (disposal should prevent new logs)
        arrayLogger.Logs.Count.Should().Be(0, "Logs should be cleared on disposal");

        // IsEnabled should return false for disposed logger
        arrayLogger.IsEnabled(LogLevel.Information).Should().BeFalse();
    }
}
