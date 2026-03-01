using FluentAssertions;
using Trax.Dashboard.Configuration;

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
        options.RoutePrefix.Should().Be("/trax");
        options.Title.Should().Be("Trax.Core");
    }
}
