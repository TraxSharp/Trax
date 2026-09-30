using System.Text.RegularExpressions;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class DataContextLoggingProviderTests
{
    #region DataContextLogger — direct tests

    private static DataContextLogger BuildLogger(
        out Channel<global::Trax.Effect.Models.Log.Log> channel,
        string categoryName = "MyApp.Foo",
        LogLevel minLevel = LogLevel.Information,
        HashSet<string>? exact = null,
        List<Regex>? wildcards = null
    )
    {
        channel = Channel.CreateUnbounded<global::Trax.Effect.Models.Log.Log>();
        return new DataContextLogger(
            channel.Writer,
            categoryName,
            minLevel,
            exact ?? [],
            wildcards ?? []
        );
    }

    [Test]
    public void Log_AboveMinimum_WritesToChannel()
    {
        var logger = BuildLogger(out var channel);

        logger.Log(LogLevel.Warning, new EventId(7, "evt"), "msg", null, (_, _) => "rendered");

        channel.Reader.TryRead(out var written).Should().BeTrue();
        written!.Level.Should().Be(LogLevel.Warning);
        written.Message.Should().Be("rendered");
        written.Category.Should().Be("MyApp.Foo");
        written.EventId.Should().Be(7);
    }

    [Test]
    public void Log_BelowMinimum_SkipsWrite()
    {
        var logger = BuildLogger(out var channel, minLevel: LogLevel.Warning);

        logger.Log(LogLevel.Debug, default, "x", null, (_, _) => "x");

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public void Log_EFCoreDatabaseCommandCategory_AlwaysSkipped()
    {
        // Hardcoded short-circuit to avoid persisting EF's own SQL traces (would
        // cause infinite log recursion when the logger's flush loop runs SaveChanges).
        var logger = BuildLogger(
            out var channel,
            categoryName: "Microsoft.EntityFrameworkCore.Database.Command"
        );

        logger.Log(LogLevel.Critical, default, "x", null, (_, _) => "x");

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public void Log_ExactBlacklisted_Skipped()
    {
        var logger = BuildLogger(
            out var channel,
            categoryName: "Noisy.Thing",
            exact: ["Noisy.Thing"]
        );

        logger.Log(LogLevel.Information, default, "m", null, (_, _) => "m");

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public void Log_WildcardBlacklisted_Skipped()
    {
        var pattern = new Regex(@"^Microsoft\..*$", RegexOptions.Compiled);
        var logger = BuildLogger(
            out var channel,
            categoryName: "Microsoft.SomethingNoisy",
            wildcards: [pattern]
        );

        logger.Log(LogLevel.Information, default, "m", null, (_, _) => "m");

        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public void IsEnabled_ChecksMinimumLevel()
    {
        var logger = BuildLogger(out _, minLevel: LogLevel.Warning);

        logger.IsEnabled(LogLevel.Trace).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
    }

    [Test]
    public void BeginScope_ReturnsNull()
    {
        var logger = BuildLogger(out _);

        logger.BeginScope("any").Should().BeNull();
    }

    #endregion

    #region DataContextLoggingProvider — via InMemory factory

    private sealed class FakeConfig : IDataContextLoggingProviderConfiguration
    {
        public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;
        public List<string> Blacklist { get; init; } = [];
    }

    private static (DataContextLoggingProvider provider, IDataContext context) BuildProvider(
        IDataContextLoggingProviderConfiguration config
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDataContextProviderFactory>();
        var provider = new DataContextLoggingProvider(factory, config);
        return (provider, (IDataContext)factory.Create());
    }

    [Test]
    public void CreateLogger_ReturnsConfiguredLogger_HonoringMinimumLevel()
    {
        var (provider, _) = BuildProvider(new FakeConfig { MinimumLogLevel = LogLevel.Error });

        var logger = provider.CreateLogger("Foo.Bar");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        provider.Dispose();
    }

    [Test]
    public void Constructor_BlacklistWithWildcards_BuildsRegexAndExactSets()
    {
        // Pattern with '*' becomes a regex; literal pattern goes to the exact set.
        var (provider, _) = BuildProvider(
            new FakeConfig { Blacklist = ["LiteralCategory", "EntityFramework.*"] }
        );

        // Both the literal and wildcard categories should be filtered out.
        var literalLogger = provider.CreateLogger("LiteralCategory");
        var wildcardLogger = provider.CreateLogger("EntityFramework.Internal.Stuff");
        var passLogger = provider.CreateLogger("Allowed.Category");

        literalLogger.Log(LogLevel.Warning, default, "x", null, (_, _) => "x");
        wildcardLogger.Log(LogLevel.Warning, default, "x", null, (_, _) => "x");
        passLogger.Log(LogLevel.Warning, default, "x", null, (_, _) => "x");

        // The provider's flush loop will eventually persist the un-filtered log.
        // Dispose writes what is queued before it returns.
        provider.Dispose();
    }

    [Test]
    public async Task FlushLoop_BatchesLogsToDatabase()
    {
        var (provider, context) = BuildProvider(
            new FakeConfig { MinimumLogLevel = LogLevel.Trace }
        );

        var logger = provider.CreateLogger("TestCategory");
        for (var i = 0; i < 5; i++)
            logger.Log(LogLevel.Information, default, i, null, (s, _) => $"msg {s}");

        // Poll for the flush loop to land at least one batch instead of waiting a fixed
        // window: CI scheduling can delay the background writer, and once any log is
        // persisted the flush path is known to work.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var landed = 0;
        while (DateTime.UtcNow < deadline)
        {
            context.Reset();
            landed = await context
                .Logs.AsNoTracking()
                .Where(l => l.Category == "TestCategory")
                .CountAsync();
            if (landed >= 1)
                break;
            await Task.Delay(50);
        }
        provider.Dispose();

        context.Reset();
        var logs = await context
            .Logs.AsNoTracking()
            .Where(l => l.Category == "TestCategory")
            .ToListAsync();
        logs.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// The entries logged just before shutdown are the ones that say why it happened, so stopping
    /// the sink writes what is already queued before it lets go.
    /// </summary>
    [Test]
    public async Task Dispose_WritesEveryEntryAlreadyQueued()
    {
        var (provider, context) = BuildProvider(
            new FakeConfig { MinimumLogLevel = LogLevel.Trace }
        );
        var category = $"Shutdown.{Guid.NewGuid():N}";
        var logger = provider.CreateLogger(category);

        const int count = 1000;
        for (var i = 0; i < count; i++)
            logger.Log(LogLevel.Information, default, i, null, (s, _) => $"msg {s}");

        provider.Dispose();

        context.Reset();
        var stored = await context.Logs.AsNoTracking().CountAsync(l => l.Category == category);
        stored.Should().Be(count, "entries queued before Dispose are written, not dropped");
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (provider, _) = BuildProvider(new FakeConfig());

        provider.Dispose();

        // The second call does nothing: the queue is already completed and the writer stopped.
        Action again = () => provider.Dispose();

        again.Should().NotThrow();
    }

    [Test]
    public void Dispose_WhenTheWriterFailed_ReturnsWithoutThrowing()
    {
        var factory = Substitute.For<IDataContextProviderFactory>();
        factory
            .CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IDataContext>>(_ => throw new InvalidOperationException("unreachable"));
        var provider = new DataContextLoggingProvider(factory, new FakeConfig());
        var logger = provider.CreateLogger("Writer.Failed");

        var log = () => logger.Log(LogLevel.Information, default, "m", null, (_, _) => "m");
        Action dispose = () => provider.Dispose();

        log.Should().NotThrow("logging never throws into the caller");
        dispose.Should().NotThrow("a writer that failed has stopped too");
    }

    [TestCase(
        true,
        TestName = "Dispose_WhenTheWriterCannotOpenItsContext_CancelsItWithinTheDrainBound"
    )]
    [TestCase(false, TestName = "Dispose_WhenTheWriterCannotSave_CancelsItWithinTheDrainBound")]
    public async Task Dispose_WhenTheWriterCannotFinish_CancelsItWithinTheDrainBound(
        bool stuckOpening
    )
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hang(CancellationToken token)
        {
            entered.TrySetResult();
            try
            {
                // negative-wait: a database that never answers; only the writer's cancellation ends it.
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        }

        var context = Substitute.For<IDataContext>();
        context.Logs.Returns(Substitute.For<DbSet<global::Trax.Effect.Models.Log.Log>>());
        context
            .SaveChanges(Arg.Any<CancellationToken>())
            .Returns(call => Hang(call.Arg<CancellationToken>()));
        var factory = Substitute.For<IDataContextProviderFactory>();
        if (stuckOpening)
            factory
                .CreateDbContextAsync(Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    await Hang(call.Arg<CancellationToken>());
                    return context;
                });
        else
            factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(context);
        var provider = new DataContextLoggingProvider(factory, new FakeConfig());

        provider
            .CreateLogger("Writer.Stuck")
            .Log(LogLevel.Information, default, "m", null, (_, _) => "m");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // measuring-interval: the stopwatch measures how long shutdown is held up.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        provider.Dispose();
        clock.Stop();

        cancelled
            .Task.IsCompleted.Should()
            .BeTrue("the writer is cancelled once the drain runs out");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }

    #endregion
}
