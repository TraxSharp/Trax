using Cronos;
using FluentAssertions;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class CronParserTests
{
    #region DetectFormat

    [TestCase("* * * * *")]
    [TestCase("0 3 * * *")]
    [TestCase("0 */6 * * *")]
    public void DetectFormat_5Field_ReturnsStandard(string expression)
    {
        CronParser.DetectFormat(expression).Should().Be(CronFormat.Standard);
    }

    [TestCase("* * * * * *")]
    [TestCase("*/15 * * * * *")]
    [TestCase("0 0 3 * * *")]
    public void DetectFormat_6Field_ReturnsIncludeSeconds(string expression)
    {
        CronParser.DetectFormat(expression).Should().Be(CronFormat.IncludeSeconds);
    }

    [TestCase("invalid")]
    [TestCase("* * *")]
    [TestCase("* * * *")]
    [TestCase("* * * * * * *")]
    public void DetectFormat_InvalidFieldCount_ReturnsNull(string expression)
    {
        CronParser.DetectFormat(expression).Should().BeNull();
    }

    #endregion

    #region IncludesSeconds

    [TestCase("* * * * *", false)]
    [TestCase("0 3 * * *", false)]
    [TestCase("*/15 * * * * *", true)]
    [TestCase("0 0 3 * * *", true)]
    public void IncludesSeconds_DetectsCorrectly(string expression, bool expected)
    {
        CronParser.IncludesSeconds(expression).Should().Be(expected);
    }

    #endregion

    #region TryParse

    [TestCase("* * * * *")]
    [TestCase("0 3 * * *")]
    [TestCase("30 * * * *")]
    [TestCase("*/15 * * * * *")]
    [TestCase("0 0 3 * * *")]
    [TestCase("30 15 9 * * 1")]
    public void TryParse_ValidExpression_ReturnsNonNull(string expression)
    {
        CronParser.TryParse(expression).Should().NotBeNull();
    }

    [TestCase("invalid")]
    [TestCase("* * *")]
    [TestCase("* * * * * * *")]
    [TestCase("99 99 99 99 99")]
    public void TryParse_InvalidExpression_ReturnsNull(string expression)
    {
        CronParser.TryParse(expression).Should().BeNull();
    }

    #endregion

    #region Parse

    [Test]
    public void Parse_ValidExpression_ReturnsExpression()
    {
        var result = CronParser.Parse("0 3 * * *");
        result.Should().NotBeNull();
    }

    [Test]
    public void Parse_InvalidExpression_ThrowsFormatException()
    {
        var act = () => CronParser.Parse("invalid");
        act.Should().Throw<FormatException>();
    }

    #endregion

    #region GetNextOccurrence

    [Test]
    public void GetNextOccurrence_5Field_ReturnsCorrectTime()
    {
        // "0 3 * * *" = daily at 3:00am
        var after = new DateTime(2026, 3, 2, 2, 59, 0, DateTimeKind.Utc);
        var next = CronParser.GetNextOccurrence("0 3 * * *", after);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void GetNextOccurrence_6Field_EveryMinuteAtSecond30_ReturnsCorrectTime()
    {
        // "30 * * * * *" = every minute at second 30
        var after = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var next = CronParser.GetNextOccurrence("30 * * * * *", after);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 3, 2, 12, 0, 30, DateTimeKind.Utc));
    }

    [Test]
    public void GetNextOccurrence_6Field_Every15Seconds_ReturnsCorrectTime()
    {
        // "*/15 * * * * *" = every 15 seconds
        var after = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var next = CronParser.GetNextOccurrence("*/15 * * * * *", after);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 3, 2, 12, 0, 15, DateTimeKind.Utc));
    }

    [Test]
    public void GetNextOccurrence_InvalidExpression_ReturnsNull()
    {
        var next = CronParser.GetNextOccurrence("invalid", DateTime.UtcNow);
        next.Should().BeNull();
    }

    #endregion
}
