using Trax.Dashboard.Configuration;
using FluentAssertions;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardOptionsTests
{
    [Test]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new DashboardOptions();

        // Assert
        options.RoutePrefix.Should().Be("/chainsharp");
        options.Title.Should().Be("Trax.Core");
    }
}
