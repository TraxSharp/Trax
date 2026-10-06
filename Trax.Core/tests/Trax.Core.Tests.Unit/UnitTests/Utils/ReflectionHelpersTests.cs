using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Utils;

namespace Trax.Core.Tests.Unit.UnitTests.Utils;

public class ReflectionHelpersTests : TestSetup
{
    [Theory]
    public async Task ExtractJunctionTypeArguments_ValidStep_ReturnsTuple()
    {
        // Act
        var (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TestJunction>();

        // Assert
        tIn.Should().Be(typeof(string));
        tOut.Should().Be(typeof(int));
    }

    [Theory]
    public async Task ExtractJunctionTypeArguments_NonStepType_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var act = () => ReflectionHelpers.ExtractJunctionTypeArguments<NotAJunction>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    public async Task TryGetRightFromEither_RightValue_ReturnsTheValue()
    {
        // Arrange
        Either<Exception, int> either = 42;

        // Act
        var found = ReflectionHelpers.TryGetRightFromEither(either, out var value);

        // Assert
        found.Should().BeTrue();
        value.Should().Be(42);
    }

    [Theory]
    public async Task TryGetRightFromEither_LeftValue_ReturnsFalse()
    {
        // Arrange
        Either<Exception, int> either = new Exception("fail");

        // Act
        var found = ReflectionHelpers.TryGetRightFromEither(either, out var value);

        // Assert
        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Theory]
    public async Task TryGetRightFromEither_NotAnEither_ReturnsFalse()
    {
        ReflectionHelpers.TryGetRightFromEither("not an either", out _).Should().BeFalse();
    }

    #region Test helpers

    private class TestJunction : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class NotAJunction { }

    #endregion
}
