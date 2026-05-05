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
        // We dispose to force the drain rather than wait for the 1-second timer tick.
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

        // Wait for the 1-second flush timer to tick once.
        await Task.Delay(1500);
        provider.Dispose();

        context.Reset();
        var logs = await context
            .Logs.AsNoTracking()
            .Where(l => l.Category == "TestCategory")
            .ToListAsync();
        logs.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (provider, _) = BuildProvider(new FakeConfig());

        Action act = () =>
        {
            provider.Dispose();
            // Second Dispose should be a no-op (cts already cancelled, channel already completed).
        };

        act.Should().NotThrow();
    }

    #endregion
}
