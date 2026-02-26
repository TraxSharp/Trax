using System.Threading.Channels;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Tests.MemoryLeak.Integration.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests for the DataContextLoggingProvider's channel-based batching behavior.
/// Verifies that the provider does not leak memory from fire-and-forget log writes
/// and correctly batches log entries into database context flushes.
/// </summary>
[TestFixture]
public class DataContextLoggerMemoryTests
{
    [Test]
    public async Task DataContextLogger_ShouldNotLeakMemory_WithHighVolumeLogging()
    {
        var factory = new InMemoryContextProviderFactory();
        var config = new DataContextLoggingProviderConfiguration
        {
            MinimumLogLevel = LogLevel.Information,
        };

        var result = await MemoryProfiler.MonitorMemoryUsageAsync(
            async () =>
            {
                using var provider = new DataContextLoggingProvider(factory, config);
                var logger = provider.CreateLogger("MemoryTest");

                for (int i = 0; i < 5000; i++)
                    logger.LogInformation("Log message {Index}", i);

                // Give the flush loop time to drain
                await Task.Delay(500);
            },
            "DataContextLogger_ChannelBatching"
        );

        Console.WriteLine(result.GetSummary());

        // Memory should be bounded — not accumulating unbounded state machines
        result
            .MemoryRetained.Should()
            .BeLessThan(
                5 * 1024 * 1024,
                "Channel-based logger should not retain significant memory after disposal"
            );
    }

    [Test]
    public void DataContextLogger_ShouldNotBlock_OnHighVolume()
    {
        var factory = new InMemoryContextProviderFactory();
        var config = new DataContextLoggingProviderConfiguration
        {
            MinimumLogLevel = LogLevel.Information,
        };

        using var provider = new DataContextLoggingProvider(factory, config);
        var logger = provider.CreateLogger("HighVolumeTest");

        // This should complete near-instantly since writes are non-blocking
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 10_000; i++)
            logger.LogInformation("High volume log {Index}", i);

        sw.Stop();

        sw.ElapsedMilliseconds.Should()
            .BeLessThan(1000, "Log writes should be non-blocking (channel enqueue only)");
    }

    [Test]
    public void DataContextLogger_ShouldRespectMinimumLogLevel()
    {
        var channel = Channel.CreateUnbounded<Trax.Effect.Models.Log.Log>();
        var logger = new DataContextLogger(
            channel.Writer,
            "TestCategory",
            LogLevel.Warning,
            [],
            []
        );

        logger.LogInformation("Should be filtered");
        logger.LogWarning("Should be kept");

        channel.Reader.TryRead(out _).Should().BeTrue("Warning level should pass the filter");
        channel.Reader.TryRead(out _).Should().BeFalse("Only one log should pass the filter");
    }

    [Test]
    public void DataContextLogger_ShouldFilterEfCoreDatabaseCommandCategory()
    {
        var channel = Channel.CreateUnbounded<Trax.Effect.Models.Log.Log>();
        var logger = new DataContextLogger(
            channel.Writer,
            "Microsoft.EntityFrameworkCore.Database.Command",
            LogLevel.Information,
            [],
            []
        );

        logger.LogInformation("EF Core command log");

        channel
            .Reader.TryRead(out _)
            .Should()
            .BeFalse("EF Core Database.Command logs should always be filtered");
    }

    [Test]
    public async Task DataContextLoggingProvider_Dispose_ShouldNotThrow()
    {
        var factory = new InMemoryContextProviderFactory();
        var config = new DataContextLoggingProviderConfiguration
        {
            MinimumLogLevel = LogLevel.Information,
        };

        var provider = new DataContextLoggingProvider(factory, config);
        var logger = provider.CreateLogger("DisposeTest");

        // Write some logs
        for (int i = 0; i < 50; i++)
            logger.LogInformation("Pre-dispose log {Index}", i);

        // Dispose should wait for flush loop to drain (up to 5s)
        provider.Dispose();

        // After dispose, writing should not throw (channel is completed, TryWrite returns false)
        var act = () => logger.LogInformation("Post-dispose log");
        act.Should().NotThrow("Logging after disposal should silently drop messages");
    }
}
