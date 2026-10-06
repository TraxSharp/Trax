using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.LogLevels;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The runtime log levels the dashboard's server settings and the GraphQL API share. A level set
/// here reaches the host's loggers through the logger filter options, over every configuration
/// source and across a configuration reload, and only a configured category can be set.
/// </summary>
[TestFixture]
public class LogLevelServiceTests
{
    private IConfigurationRoot _configuration = null!;
    private ServiceProvider _services = null!;
    private ILogLevelService _levels = null!;

    [SetUp]
    public void SetUp()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Information",
                    ["Logging:LogLevel:Trax.Orders"] = "Warning",
                    ["Logging:LogLevel:Microsoft"] = "Error",
                    ["Logging:LogLevel:Pinned"] = "Warning",
                }
            )
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(_configuration)
            .AddLogging(b =>
                b.AddConfiguration(_configuration.GetSection("Logging"))
                    .AddProvider(new AllLevelsProvider())
            );
        SchedulerConfigurationBuilder.RegisterLogLevelOverrides(services);
        // A host that pins a category's filter itself, after Trax.
        services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>>(
            new PinCategory("Pinned", LogLevel.Critical)
        );

        _services = services.BuildServiceProvider();
        _levels = _services.GetRequiredService<ILogLevelService>();
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public void Levels_list_the_configured_categories_default_first()
    {
        var levels = _levels.GetLogLevels();

        levels
            .Select(l => l.Category)
            .Should()
            .Equal("Default", "Microsoft", "Pinned", "Trax.Orders");
        levels.Single(l => l.Category == "Trax.Orders").Level.Should().Be(LogLevel.Warning);
        levels.Single(l => l.Category == "Trax.Orders").ConfiguredLevel.Should().Be("Warning");
        levels.Should().OnlyContain(l => !l.Overridden);
    }

    [Test]
    public void A_level_set_here_reaches_the_loggers_and_reads_back_overridden()
    {
        var logger = _services.GetRequiredService<ILoggerFactory>().CreateLogger("Trax.Orders");
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();

        var result = _levels.SetLogLevels([new LogLevelChange("trax.orders", LogLevel.Debug)]);

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        result.NotApplied.Should().BeEmpty();
        logger.IsEnabled(LogLevel.Debug).Should().BeTrue("the logger factory re-read its filters");
        var entry = _levels.GetLogLevels().Single(l => l.Category == "Trax.Orders");
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Overridden.Should().BeTrue();
        entry.ConfiguredLevel.Should().Be("Warning");
    }

    [Test]
    public void A_level_set_here_survives_a_configuration_reload()
    {
        _levels.SetLogLevels([new LogLevelChange("Microsoft", LogLevel.Trace)]);

        _configuration.Reload();

        _levels
            .GetLogLevels()
            .Single(l => l.Category == "Microsoft")
            .Level.Should()
            .Be(LogLevel.Trace);
    }

    [Test]
    public void A_category_the_host_filters_itself_is_reported_not_applied()
    {
        var result = _levels.SetLogLevels([new LogLevelChange("Pinned", LogLevel.Debug)]);

        result.Success.Should().BeTrue();
        result.NotApplied.Should().Equal("Pinned");
        result.Message.Should().Contain("Pinned");
    }

    [Test]
    public void An_unconfigured_category_refuses_the_whole_list()
    {
        var result = _levels.SetLogLevels([
            new LogLevelChange("Trax.Orders", LogLevel.Debug),
            new LogLevelChange("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Trace),
        ]);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Microsoft.EntityFrameworkCore.Database.Command");
        _levels.GetLogLevels().Should().OnlyContain(l => !l.Overridden, "nothing was set");
    }

    [Test]
    public void An_undefined_level_or_an_empty_list_is_refused()
    {
        _levels
            .SetLogLevels([new LogLevelChange("Default", (LogLevel)42)])
            .Success.Should()
            .BeFalse();
        _levels.SetLogLevels([]).Success.Should().BeFalse();
    }

    /// <summary>
    /// A provider whose loggers take every level, so whether a level is on is decided by the
    /// filters alone.
    /// </summary>
    private sealed class AllLevelsProvider : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }

        public void Dispose() { }
    }

    private sealed class PinCategory(string category, LogLevel level)
        : IPostConfigureOptions<LoggerFilterOptions>
    {
        public void PostConfigure(string? name, LoggerFilterOptions options) =>
            options.Rules.Add(new LoggerFilterRule(null, category, level, null));
    }
}
