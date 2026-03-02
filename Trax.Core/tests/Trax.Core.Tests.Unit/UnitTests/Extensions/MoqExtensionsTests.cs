using FluentAssertions;
using Moq;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class MoqExtensionsTests : TestSetup
{
    [Theory]
    public async Task IsMoqProxy_WithMoqObject_ReturnsTrue()
    {
        // Arrange
        var mock = new Mock<IDisposable>();
        var proxyType = mock.Object.GetType();

        // Act
        var result = proxyType.IsMoqProxy();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task IsMoqProxy_WithRegularType_ReturnsFalse()
    {
        // Arrange & Act
        var result = typeof(string).IsMoqProxy();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    public async Task GetMockedTypeFromObject_WithMockObject_ReturnsMockedType()
    {
        // Arrange
        var mock = new Mock<IDisposable>();

        // Act
        var mockedType = mock.Object.GetMockedTypeFromObject();

        // Assert
        mockedType.Should().Be(typeof(IDisposable));
    }

    [Theory]
    public async Task GetMockedTypeFromObject_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        object? nullObject = null;

        // Act & Assert
        var act = () => nullObject!.GetMockedTypeFromObject();
        act.Should().Throw<ArgumentNullException>();
    }
}
