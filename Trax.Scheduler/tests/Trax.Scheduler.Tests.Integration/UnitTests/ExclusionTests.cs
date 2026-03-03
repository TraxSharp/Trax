using FluentAssertions;
using Trax.Effect.Models.Manifest;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ExclusionTests
{
    // ── DaysOfWeek ──────────────────────────────────────────────────

    [Test]
    public void IsExcluded_WhenDayOfWeekMatches_ReturnsTrue()
    {
        var exclusion = Exclude.DaysOfWeek(DayOfWeek.Saturday);
        var saturday = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(saturday).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenDayOfWeekDoesNotMatch_ReturnsFalse()
    {
        var exclusion = Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday);
        var monday = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(monday).Should().BeFalse();
    }

    // ── Dates ───────────────────────────────────────────────────────

    [Test]
    public void IsExcluded_WhenDateMatches_ReturnsTrue()
    {
        var exclusion = Exclude.Dates(new DateOnly(2026, 12, 25));
        var christmas = new DateTime(2026, 12, 25, 15, 30, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(christmas).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenDateDoesNotMatch_ReturnsFalse()
    {
        var exclusion = Exclude.Dates(new DateOnly(2026, 12, 25));
        var boxingDay = new DateTime(2026, 12, 26, 10, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(boxingDay).Should().BeFalse();
    }

    // ── DateRange ───────────────────────────────────────────────────

    [Test]
    public void IsExcluded_WhenWithinDateRange_ReturnsTrue()
    {
        var exclusion = Exclude.DateRange(new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2));
        var christmas = new DateTime(2026, 12, 25, 12, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(christmas).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenOutsideDateRange_ReturnsFalse()
    {
        var exclusion = Exclude.DateRange(new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2));
        var beforeRange = new DateTime(2026, 12, 22, 23, 59, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(beforeRange).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_WhenOnDateRangeStartBoundary_ReturnsTrue()
    {
        var exclusion = Exclude.DateRange(new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2));
        var startDay = new DateTime(2026, 12, 23, 0, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(startDay).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenOnDateRangeEndBoundary_ReturnsTrue()
    {
        var exclusion = Exclude.DateRange(new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2));
        var endDay = new DateTime(2027, 1, 2, 23, 59, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(endDay).Should().BeTrue();
    }

    // ── TimeWindow ──────────────────────────────────────────────────

    [Test]
    public void IsExcluded_WhenWithinTimeWindow_ReturnsTrue()
    {
        var exclusion = Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00"));
        var insideWindow = new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(insideWindow).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenOutsideTimeWindow_ReturnsFalse()
    {
        var exclusion = Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00"));
        var outsideWindow = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(outsideWindow).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_WhenTimeWindowCrossesMidnight_InsideBeforeMidnight_ReturnsTrue()
    {
        var exclusion = Exclude.TimeWindow(TimeOnly.Parse("23:00"), TimeOnly.Parse("02:00"));
        var beforeMidnight = new DateTime(2026, 3, 2, 23, 30, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(beforeMidnight).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenTimeWindowCrossesMidnight_InsideAfterMidnight_ReturnsTrue()
    {
        var exclusion = Exclude.TimeWindow(TimeOnly.Parse("23:00"), TimeOnly.Parse("02:00"));
        var afterMidnight = new DateTime(2026, 3, 3, 1, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(afterMidnight).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_WhenTimeWindowCrossesMidnight_OutsideWindow_ReturnsFalse()
    {
        var exclusion = Exclude.TimeWindow(TimeOnly.Parse("23:00"), TimeOnly.Parse("02:00"));
        var outsideWindow = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        exclusion.IsExcluded(outsideWindow).Should().BeFalse();
    }

    // ── JSON Serialization Round-Trip ───────────────────────────────

    [Test]
    public void Exclusions_SerializeAndDeserialize_RoundTrip()
    {
        var manifest = new Manifest { ExternalId = Guid.NewGuid().ToString("N"), Name = "Test" };
        var exclusions = new List<Exclusion>
        {
            Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday),
            Exclude.Dates(new DateOnly(2026, 12, 25)),
            Exclude.DateRange(new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2)),
            Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00")),
        };

        manifest.SetExclusions(exclusions);
        var deserialized = manifest.GetExclusions();

        deserialized.Should().HaveCount(4);

        deserialized[0].Type.Should().Be(ExclusionType.DaysOfWeek);
        deserialized[0].DaysOfWeek.Should().Contain(DayOfWeek.Saturday);
        deserialized[0].DaysOfWeek.Should().Contain(DayOfWeek.Sunday);

        deserialized[1].Type.Should().Be(ExclusionType.Dates);
        deserialized[1].Dates.Should().Contain(new DateOnly(2026, 12, 25));

        deserialized[2].Type.Should().Be(ExclusionType.DateRange);
        deserialized[2].StartDate.Should().Be(new DateOnly(2026, 12, 23));
        deserialized[2].EndDate.Should().Be(new DateOnly(2027, 1, 2));

        deserialized[3].Type.Should().Be(ExclusionType.TimeWindow);
        deserialized[3].StartTime.Should().Be(TimeOnly.Parse("02:00"));
        deserialized[3].EndTime.Should().Be(TimeOnly.Parse("04:00"));
    }

    [Test]
    public void GetExclusions_WhenNull_ReturnsEmptyList()
    {
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "Test",
            Exclusions = null,
        };

        manifest.GetExclusions().Should().BeEmpty();
    }

    [Test]
    public void SetExclusions_WhenEmptyList_SetsNull()
    {
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "Test",
            Exclusions = "some-previous-value",
        };

        manifest.SetExclusions([]);

        manifest.Exclusions.Should().BeNull();
    }
}
