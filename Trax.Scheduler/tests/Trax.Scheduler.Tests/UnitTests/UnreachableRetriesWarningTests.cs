using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// A failing manifest is dead-lettered once more than MaxRetries failures fall inside
/// FailureCountWindow. When the retry backoff alone spaces those failures over the window or
/// more, the count can never be reached, so the host says so when it starts.
/// </summary>
[TestFixture]
public class UnreachableRetriesWarningTests
{
    [Test]
    public void The_defaults_can_reach_their_retry_count()
    {
        new SchedulerConfiguration().UnreachableRetriesWarning().Should().BeNull();
    }

    [Test]
    public void A_retry_count_the_backoff_keeps_inside_the_window_is_not_warned_about()
    {
        // 5 + 10 + 20 + 40 minutes, then 20 more at the one-hour cap: 21h15m, inside 24 hours.
        new SchedulerConfiguration { DefaultMaxRetries = 24 }
            .UnreachableRetriesWarning()
            .Should()
            .BeNull();
    }

    [Test]
    public void A_retry_count_the_backoff_spaces_past_the_window_is_warned_about()
    {
        // 5 + 10 + 20 + 40 minutes, then 26 more at the one-hour cap: 27h15m, past 24 hours.
        new SchedulerConfiguration { DefaultMaxRetries = 30 }
            .UnreachableRetriesWarning()
            .Should()
            .Contain("FailureCountWindow")
            .And.Contain("DefaultMaxRetries (30)");
    }

    [Test]
    public void A_window_shorter_than_one_retry_delay_is_warned_about()
    {
        var configuration = new SchedulerConfiguration
        {
            DefaultMaxRetries = 1,
            DefaultRetryDelay = TimeSpan.FromMinutes(10),
            FailureCountWindow = TimeSpan.FromMinutes(5),
        };

        configuration.UnreachableRetriesWarning().Should().NotBeNull();
    }

    [Test]
    public void The_largest_retry_count_is_warned_about_without_overflowing()
    {
        new SchedulerConfiguration { DefaultMaxRetries = int.MaxValue }
            .UnreachableRetriesWarning()
            .Should()
            .NotBeNull();
    }

    [Test]
    public void No_retries_is_never_warned_about()
    {
        new SchedulerConfiguration
        {
            DefaultMaxRetries = 0,
            FailureCountWindow = TimeSpan.FromSeconds(1),
        }
            .UnreachableRetriesWarning()
            .Should()
            .BeNull();
    }

    [Test]
    public async Task The_scheduler_logs_the_warning_when_it_starts()
    {
        var logger = new CapturingLogger();
        var service = new SchedulerStartupService(
            new ServiceCollection().BuildServiceProvider(),
            new SchedulerConfiguration
            {
                DefaultMaxRetries = 30,
                RecoverStuckJobsOnStartup = false,
            },
            logger
        );

        await service.StartAsync(CancellationToken.None);

        logger
            .Entries.Should()
            .Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("FailureCountWindow"));
    }

    private sealed class CapturingLogger : ILogger<SchedulerStartupService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
