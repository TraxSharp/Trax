using AwesomeAssertions;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// A retention's cutoff stays inside the range a <see cref="DateTime"/> can hold, so a retention
/// meaning "keep it for ever" selects nothing instead of throwing.
/// </summary>
[TestFixture]
public class TimeCutoffTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void An_ordinary_span_is_subtracted()
    {
        TimeCutoff.Before(Now, TimeSpan.FromHours(1)).Should().Be(Now.AddHours(-1));
    }

    [Test]
    public void A_span_past_the_start_of_the_calendar_gives_the_earliest_instant()
    {
        var cutoff = TimeCutoff.Before(Now, TimeSpan.MaxValue);

        cutoff.Should().Be(DateTime.MinValue);
        cutoff.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Test]
    public void A_negative_span_past_the_end_of_the_calendar_gives_the_latest_instant()
    {
        TimeCutoff.Before(Now, TimeSpan.MinValue).Should().Be(DateTime.MaxValue);
    }
}
