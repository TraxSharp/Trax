using System.Diagnostics;
using AwesomeAssertions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The wait between two polling cycles never ends at once, whatever interval it reads, and a
/// cancelled stopping token always ends it with false. A zero interval used to return true
/// immediately without looking at the token, so a polling loop spun and could not be stopped.
/// </summary>
[TestFixture]
public class PollingDelayTests
{
    [Test]
    public async Task A_zero_interval_with_a_cancelled_token_returns_false()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var waited = await PollingDelay
            .WaitAsync(() => TimeSpan.Zero, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        waited.Should().BeFalse();
    }

    [Test]
    public async Task A_negative_interval_with_a_cancelled_token_returns_false()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var waited = await PollingDelay
            .WaitAsync(() => TimeSpan.FromSeconds(-1), cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        waited.Should().BeFalse();
    }

    [Test]
    public async Task A_zero_interval_waits_the_shortest_polling_interval()
    {
        var elapsed = Stopwatch.StartNew();

        var waited = await PollingDelay
            .WaitAsync(() => TimeSpan.Zero, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        waited.Should().BeTrue();
        elapsed
            .Elapsed.Should()
            .BeGreaterThanOrEqualTo(
                SchedulerConfigLimits.MinTimerInterval - TimeSpan.FromMilliseconds(50)
            );
    }

    [Test]
    public async Task Cancelling_during_the_wait_returns_false()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var waited = await PollingDelay
            .WaitAsync(() => TimeSpan.FromHours(1), cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        waited.Should().BeFalse();
    }
}
