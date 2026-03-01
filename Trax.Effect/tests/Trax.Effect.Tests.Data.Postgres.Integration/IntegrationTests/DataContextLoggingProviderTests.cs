using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Effect.Tests.Data.Postgres.Integration.IntegrationTests;

/// <summary>
/// Integration tests that verify the DataContextLoggingProvider persists logs
/// to Postgres correctly under various throughput and configuration scenarios.
/// Each test creates a standalone provider instance to isolate from framework noise.
/// </summary>
/// <remarks>
/// The flush loop runs on a background Task and processes logs in batches of 256.
/// A 1-second PeriodicTimer triggers flushes for partial batches. After writing logs,
/// tests wait for the flush loop to drain before querying. Dispose() cancels the flush
/// loop immediately, so queries must happen before disposal.
/// </remarks>
[TestFixture]
public class DataContextLoggingProviderTests : TestSetup
{
    private static DataContextLoggingProvider CreateStandaloneProvider(
        IDataContextProviderFactory factory,
        LogLevel minimumLogLevel = LogLevel.Trace,
        List<string>? blacklist = null
    )
    {
        var config = new DataContextLoggingProviderConfiguration
        {
            MinimumLogLevel = minimumLogLevel,
            Blacklist = blacklist ?? [],
        };
        return new DataContextLoggingProvider(factory, config);
    }

    /// <summary>
    /// Waits for the flush loop to drain by polling the database until the expected
    /// count is reached or the timeout expires. This avoids fixed delays and accounts
    /// for flush loop startup time (CreateDbContextAsync) and batch processing.
    /// </summary>
    private static async Task<int> WaitForLogCount(
        IDataContextProviderFactory factory,
        string category,
        int expectedCount,
        TimeSpan? timeout = null
    )
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + timeout.Value;
        var count = 0;

        while (DateTime.UtcNow < deadline)
        {
            using var context = (IDataContext)factory.Create();
            count = await context.Logs.Where(l => l.Category == category).CountAsync();

            if (count >= expectedCount)
                return count;

            await Task.Delay(250);
        }

        return count;
    }

    [Test]
    public async Task BulkPersistence_AllLogsPersisted()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);

        try
        {
            var logger = provider.CreateLogger("Test.BulkPersistence");
            const int logCount = 500;

            for (var i = 0; i < logCount; i++)
                logger.LogInformation("Bulk log {Index}", i);

            var count = await WaitForLogCount(factory, "Test.BulkPersistence", logCount);

            count.Should().Be(logCount);

            using var context = (IDataContext)factory.Create();
            var sample = await context.Logs.FirstAsync(l => l.Category == "Test.BulkPersistence");
            sample.Message.Should().Contain("Bulk log");
            sample.Level.Should().Be(LogLevel.Information);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task Truncation_OversizedFields_TruncatedToColumnLimits()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);
        var longCategory = new string('C', 600);

        try
        {
            var logger = provider.CreateLogger(longCategory);
            var longMessage = new string('M', 5000);
            var longExceptionMessage = new string('E', 3000);

            logger.LogError(new Exception(longExceptionMessage), longMessage);

            // Wait for the single log to flush — category is truncated to 500 'C's
            var truncatedCategory = new string('C', 500);
            await WaitForLogCount(factory, truncatedCategory, 1);

            using var context = (IDataContext)factory.Create();
            var log = await context.Logs.FirstAsync(l => l.Category == truncatedCategory);

            log.Message.Should().HaveLength(4000);
            log.Category.Should().HaveLength(500);
            log.Exception.Should().HaveLength(2000);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task BatchFlushing_MultipleBatches_AllPersisted()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);

        try
        {
            var logger = provider.CreateLogger("Test.BatchFlushing");
            const int logCount = 700; // 3 batches: 256 + 256 + 188

            for (var i = 0; i < logCount; i++)
                logger.LogInformation("Batch log {Index}", i);

            var count = await WaitForLogCount(factory, "Test.BatchFlushing", logCount);

            count
                .Should()
                .Be(logCount, "all logs should persist across multiple 256-entry batches");
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task TimerFlush_BelowBatchThreshold_FlushedByTimer()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);

        try
        {
            var logger = provider.CreateLogger("Test.TimerFlush");
            const int logCount = 10;

            for (var i = 0; i < logCount; i++)
                logger.LogInformation("Timer log {Index}", i);

            // Wait for timer-based flush (1-second PeriodicTimer)
            var count = await WaitForLogCount(factory, "Test.TimerFlush", logCount);

            count
                .Should()
                .Be(logCount, "timer-based flush should persist logs without filling a batch");
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task BlacklistFiltering_BlacklistedCategory_NotPersisted()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(
            factory,
            blacklist: ["Test.Blacklisted", "Test.Wildcard.*"]
        );

        try
        {
            var blockedExact = provider.CreateLogger("Test.Blacklisted");
            var blockedWildcard = provider.CreateLogger("Test.Wildcard.SubCategory");
            var allowed = provider.CreateLogger("Test.Allowed");

            for (var i = 0; i < 5; i++)
            {
                blockedExact.LogInformation("Should not persist {Index}", i);
                blockedWildcard.LogInformation("Should not persist {Index}", i);
                allowed.LogInformation("Should persist {Index}", i);
            }

            // Wait for the allowed logs to flush
            await WaitForLogCount(factory, "Test.Allowed", 5);

            using var context = (IDataContext)factory.Create();
            var logs = await context
                .Logs.Where(l =>
                    l.Category == "Test.Blacklisted"
                    || l.Category == "Test.Wildcard.SubCategory"
                    || l.Category == "Test.Allowed"
                )
                .ToListAsync();

            logs.Should().HaveCount(5);
            logs.Should().OnlyContain(l => l.Category == "Test.Allowed");
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task HighThroughput_ChannelCapacity_AllLogsPersisted()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);

        try
        {
            var logger = provider.CreateLogger("Test.HighThroughput");
            const int logCount = 4096; // channel capacity

            for (var i = 0; i < logCount; i++)
                logger.LogInformation("High throughput log {Index}", i);

            var count = await WaitForLogCount(factory, "Test.HighThroughput", logCount);

            count
                .Should()
                .Be(logCount, "flush loop should drain concurrently preventing any drops");
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task DropOldest_ExceedsCapacity_SomeLogsDropped()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = CreateStandaloneProvider(factory);

        try
        {
            var logger = provider.CreateLogger("Test.DropOldest");
            const int logCount = 20_000; // far exceeds 4096 channel capacity

            // Tight loop to overwhelm the channel faster than flush can drain
            for (var i = 0; i < logCount; i++)
                logger.LogInformation("Overflow log {Index}", i);

            // Wait for flush loop to drain what it can (generous timeout)
            await Task.Delay(5000);

            using var context = (IDataContext)factory.Create();
            var count = await context.Logs.Where(l => l.Category == "Test.DropOldest").CountAsync();

            count.Should().BeGreaterThan(0, "some logs should survive");
            count
                .Should()
                .BeLessThan(logCount, "DropOldest should discard some logs when channel overflows");
        }
        finally
        {
            provider.Dispose();
        }
    }
}
