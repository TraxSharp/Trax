using FluentAssertions;
using LanguageExt;
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
    public async Task GetRightFromDynamicEither_RightValue_ReturnsSome()
    {
        // Arrange
        Either<Exception, int> either = 42;

        // Act
        var result = ReflectionHelpers.GetRightFromDynamicEither(either);

        // Assert
        result.IsSome.Should().BeTrue();
    }

    [Theory]
    public async Task GetRightFromDynamicEither_LeftValue_ReturnsNone()
    {
        // Arrange
        Either<Exception, int> either = new Exception("fail");

        // Act
        var result = ReflectionHelpers.GetRightFromDynamicEither(either);

        // Assert
        result.IsNone.Should().BeTrue();
    }

    #region Test helpers

    private class TestJunction : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class NotAJunction { }

    #endregion
}
