using FluentAssertions;
using Radzen;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardFormattersTests
{
    [Test]
    public void GetDeadLetterStatusBadgeStyle_AwaitingIntervention_ReturnsWarning()
    {
        // Act
        var result = DashboardFormatters.GetDeadLetterStatusBadgeStyle(
            DeadLetterStatus.AwaitingIntervention
        );

        // Assert
        result.Should().Be(BadgeStyle.Warning);
    }

    [Test]
    public void GetDeadLetterStatusBadgeStyle_Retried_ReturnsInfo()
    {
        // Act
        var result = DashboardFormatters.GetDeadLetterStatusBadgeStyle(DeadLetterStatus.Retried);

        // Assert
        result.Should().Be(BadgeStyle.Info);
    }

    [Test]
    public void GetDeadLetterStatusBadgeStyle_Acknowledged_ReturnsSuccess()
    {
        // Act
        var result = DashboardFormatters.GetDeadLetterStatusBadgeStyle(
            DeadLetterStatus.Acknowledged
        );

        // Assert
        result.Should().Be(BadgeStyle.Success);
    }

    [Test]
    public void GetDeadLetterStatusBadgeStyle_UnknownValue_ReturnsLight()
    {
        // Act
        var result = DashboardFormatters.GetDeadLetterStatusBadgeStyle((DeadLetterStatus)99);

        // Assert
        result.Should().Be(BadgeStyle.Light);
    }

    [Test]
    public void ShortName_WithDottedName_ReturnsLastSegment()
    {
        // Act
        var result = DashboardFormatters.ShortName("Trax.Samples.MyTrain");

        // Assert
        result.Should().Be("MyTrain");
    }

    [Test]
    public void ShortName_WithSimpleName_ReturnsSameName()
    {
        // Act
        var result = DashboardFormatters.ShortName("MyTrain");

        // Assert
        result.Should().Be("MyTrain");
    }

    [Test]
    public void FormatDuration_Milliseconds_FormatsCorrectly()
    {
        // Act
        var result = DashboardFormatters.FormatDuration(450);

        // Assert
        result.Should().Be("450ms");
    }

    [Test]
    public void FormatDuration_Seconds_FormatsCorrectly()
    {
        // Act
        var result = DashboardFormatters.FormatDuration(5500);

        // Assert
        result.Should().Be("5.5s");
    }

    [Test]
    public void FormatDuration_Minutes_FormatsCorrectly()
    {
        // Act
        var result = DashboardFormatters.FormatDuration(90_000);

        // Assert
        result.Should().Be("1.5m");
    }

    [Test]
    public void FormatJson_ValidJson_ReturnsPrettyPrinted()
    {
        // Act
        var result = DashboardFormatters.FormatJson("{\"key\":\"value\"}");

        // Assert
        result.Should().Contain("\"key\"");
        result.Should().Contain("\"value\"");
        result.Should().Contain("\n"); // pretty-printed has newlines
    }

    [Test]
    public void FormatJson_InvalidJson_ReturnsOriginalString()
    {
        // Arrange
        var invalid = "not json at all";

        // Act
        var result = DashboardFormatters.FormatJson(invalid);

        // Assert
        result.Should().Be(invalid);
    }

    #region GetStateBadgeStyle Tests

    [Test]
    public void GetStateBadgeStyle_Completed_ReturnsSuccess()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle(TrainState.Completed);

        // Assert
        result.Should().Be(BadgeStyle.Success);
    }

    [Test]
    public void GetStateBadgeStyle_Failed_ReturnsDanger()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle(TrainState.Failed);

        // Assert
        result.Should().Be(BadgeStyle.Danger);
    }

    [Test]
    public void GetStateBadgeStyle_InProgress_ReturnsInfo()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle(TrainState.InProgress);

        // Assert
        result.Should().Be(BadgeStyle.Info);
    }

    [Test]
    public void GetStateBadgeStyle_Pending_ReturnsWarning()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle(TrainState.Pending);

        // Assert
        result.Should().Be(BadgeStyle.Warning);
    }

    [Test]
    public void GetStateBadgeStyle_Cancelled_ReturnsWarning()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle(TrainState.Cancelled);

        // Assert
        result.Should().Be(BadgeStyle.Warning);
    }

    [Test]
    public void GetStateBadgeStyle_UnknownValue_ReturnsLight()
    {
        // Act
        var result = DashboardFormatters.GetStateBadgeStyle((TrainState)99);

        // Assert
        result.Should().Be(BadgeStyle.Light);
    }

    [Test]
    public void GetStateBadgeStyle_AllDefinedStates_ReturnExpectedStyles()
    {
        // Verify every defined TrainState maps to a non-default style
        var expectedMappings = new Dictionary<TrainState, BadgeStyle>
        {
            [TrainState.Completed] = BadgeStyle.Success,
            [TrainState.Failed] = BadgeStyle.Danger,
            [TrainState.InProgress] = BadgeStyle.Info,
            [TrainState.Pending] = BadgeStyle.Warning,
            [TrainState.Cancelled] = BadgeStyle.Warning,
        };

        foreach (var (state, expectedStyle) in expectedMappings)
        {
            var result = DashboardFormatters.GetStateBadgeStyle(state);
            result.Should().Be(expectedStyle, $"TrainState.{state} should map to {expectedStyle}");
        }
    }

    #endregion
}
