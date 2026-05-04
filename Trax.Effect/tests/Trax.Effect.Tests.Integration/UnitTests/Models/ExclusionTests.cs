using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Models.Manifest;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class ExclusionTests
{
    private static readonly DateTime Saturday = new(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc); // 2026-05-09 is Saturday
    private static readonly DateTime Wednesday = new(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);

    #region DaysOfWeek

    [Test]
    public void IsExcluded_DaysOfWeekMatch_ReturnsTrue()
    {
        var ex = Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday);

        ex.IsExcluded(Saturday).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_DaysOfWeekNoMatch_ReturnsFalse()
    {
        var ex = Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday);

        ex.IsExcluded(Wednesday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_DaysOfWeekNullList_ReturnsFalse()
    {
        var ex = new Exclusion { Type = ExclusionType.DaysOfWeek, DaysOfWeek = null };

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    #endregion

    #region Dates

    [Test]
    public void IsExcluded_DatesMatch_ReturnsTrue()
    {
        var ex = Exclude.Dates(new DateOnly(2026, 5, 9));

        ex.IsExcluded(Saturday).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_DatesNoMatch_ReturnsFalse()
    {
        var ex = Exclude.Dates(new DateOnly(2026, 5, 10));

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_DatesNullList_ReturnsFalse()
    {
        var ex = new Exclusion { Type = ExclusionType.Dates, Dates = null };

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    #endregion

    #region DateRange

    [Test]
    public void IsExcluded_DateInRange_ReturnsTrue()
    {
        var ex = Exclude.DateRange(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        ex.IsExcluded(Saturday).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_DateBeforeRange_ReturnsFalse()
    {
        var ex = Exclude.DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_DateAfterRange_ReturnsFalse()
    {
        var ex = Exclude.DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_DateRangeMissingStart_ReturnsFalse()
    {
        var ex = new Exclusion
        {
            Type = ExclusionType.DateRange,
            EndDate = new DateOnly(2026, 12, 31),
        };

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    #endregion

    #region TimeWindow

    [Test]
    public void IsExcluded_TimeInWindow_ReturnsTrue()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(11, 0), new TimeOnly(13, 0));

        ex.IsExcluded(Saturday).Should().BeTrue(); // Saturday is 12:00
    }

    [Test]
    public void IsExcluded_TimeOutsideWindow_ReturnsFalse()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(2, 0), new TimeOnly(4, 0));

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_TimeWindowAcrossMidnight_NowAfterStart()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(23, 0), new TimeOnly(2, 0));
        var lateNight = new DateTime(2026, 5, 9, 23, 30, 0, DateTimeKind.Utc);

        ex.IsExcluded(lateNight).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_TimeWindowAcrossMidnight_NowBeforeEnd()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(23, 0), new TimeOnly(2, 0));
        var earlyMorning = new DateTime(2026, 5, 9, 1, 0, 0, DateTimeKind.Utc);

        ex.IsExcluded(earlyMorning).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_TimeWindowAcrossMidnight_OutsideWindow()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(23, 0), new TimeOnly(2, 0));
        var midDay = new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc);

        ex.IsExcluded(midDay).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_TimeWindowAtStart_Inclusive()
    {
        var ex = Exclude.TimeWindow(new TimeOnly(12, 0), new TimeOnly(14, 0));

        ex.IsExcluded(Saturday).Should().BeTrue();
    }

    [Test]
    public void IsExcluded_TimeWindowAtEnd_Exclusive()
    {
        // End is exclusive — at exactly 14:00 with window 12:00-14:00 should NOT match
        var ex = Exclude.TimeWindow(new TimeOnly(10, 0), new TimeOnly(12, 0));

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    [Test]
    public void IsExcluded_TimeWindowMissingStart_ReturnsFalse()
    {
        var ex = new Exclusion { Type = ExclusionType.TimeWindow, EndTime = new TimeOnly(2, 0) };

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    #endregion

    #region Default / unknown type

    [Test]
    public void IsExcluded_UnknownType_ReturnsFalse()
    {
        var ex = new Exclusion { Type = (ExclusionType)999 };

        ex.IsExcluded(Saturday).Should().BeFalse();
    }

    #endregion

    #region Factories

    [Test]
    public void Factory_DaysOfWeek_PopulatesType()
    {
        var ex = Exclude.DaysOfWeek(DayOfWeek.Monday);

        ex.Type.Should().Be(ExclusionType.DaysOfWeek);
        ex.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday });
    }

    [Test]
    public void Factory_Dates_PopulatesType()
    {
        var d = new DateOnly(2026, 1, 1);
        var ex = Exclude.Dates(d);

        ex.Type.Should().Be(ExclusionType.Dates);
        ex.Dates.Should().BeEquivalentTo(new[] { d });
    }

    [Test]
    public void Factory_DateRange_PopulatesStartAndEnd()
    {
        var s = new DateOnly(2026, 1, 1);
        var e = new DateOnly(2026, 1, 31);
        var ex = Exclude.DateRange(s, e);

        ex.Type.Should().Be(ExclusionType.DateRange);
        ex.StartDate.Should().Be(s);
        ex.EndDate.Should().Be(e);
    }

    [Test]
    public void Factory_TimeWindow_PopulatesStartAndEnd()
    {
        var s = new TimeOnly(2, 0);
        var e = new TimeOnly(4, 0);
        var ex = Exclude.TimeWindow(s, e);

        ex.Type.Should().Be(ExclusionType.TimeWindow);
        ex.StartTime.Should().Be(s);
        ex.EndTime.Should().Be(e);
    }

    #endregion
}
