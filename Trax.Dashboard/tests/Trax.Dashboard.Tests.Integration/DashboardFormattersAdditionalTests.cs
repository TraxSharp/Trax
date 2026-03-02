using FluentAssertions;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Utilities;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardFormattersAdditionalTests
{
    #region FormatUptime

    [Test]
    public void FormatUptime_Days_FormatsCorrectly()
    {
        // Arrange
        var uptime = TimeSpan.FromDays(2) + TimeSpan.FromHours(3);

        // Act
        var result = DashboardFormatters.FormatUptime(uptime);

        // Assert
        result.Should().Be("2d 3h");
    }

    [Test]
    public void FormatUptime_Hours_FormatsCorrectly()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);

        // Act
        var result = DashboardFormatters.FormatUptime(uptime);

        // Assert
        result.Should().Be("5h 30m");
    }

    [Test]
    public void FormatUptime_Minutes_FormatsCorrectly()
    {
        // Arrange
        var uptime = TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(45);

        // Act
        var result = DashboardFormatters.FormatUptime(uptime);

        // Assert
        result.Should().Be("15m 45s");
    }

    #endregion

    #region GetLogLevelBadgeStyle

    [Test]
    public void GetLogLevelBadgeStyle_Critical_ReturnsDanger()
    {
        DashboardFormatters.GetLogLevelBadgeStyle(LogLevel.Critical).Should().Be(BadgeStyle.Danger);
    }

    [Test]
    public void GetLogLevelBadgeStyle_Error_ReturnsDanger()
    {
        DashboardFormatters.GetLogLevelBadgeStyle(LogLevel.Error).Should().Be(BadgeStyle.Danger);
    }

    [Test]
    public void GetLogLevelBadgeStyle_Warning_ReturnsWarning()
    {
        DashboardFormatters.GetLogLevelBadgeStyle(LogLevel.Warning).Should().Be(BadgeStyle.Warning);
    }

    [Test]
    public void GetLogLevelBadgeStyle_Information_ReturnsInfo()
    {
        DashboardFormatters
            .GetLogLevelBadgeStyle(LogLevel.Information)
            .Should()
            .Be(BadgeStyle.Info);
    }

    [Test]
    public void GetLogLevelBadgeStyle_Debug_ReturnsLight()
    {
        DashboardFormatters.GetLogLevelBadgeStyle(LogLevel.Debug).Should().Be(BadgeStyle.Light);
    }

    [Test]
    public void GetLogLevelBadgeStyle_Trace_ReturnsLight()
    {
        DashboardFormatters.GetLogLevelBadgeStyle(LogLevel.Trace).Should().Be(BadgeStyle.Light);
    }

    #endregion
}
