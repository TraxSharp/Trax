using FluentAssertions;
using LanguageExt;
using Trax.Core.Step;
using Trax.Core.Utils;

namespace Trax.Core.Tests.Unit.UnitTests.Utils;

public class ReflectionHelpersTests : TestSetup
{
    [Theory]
    public async Task ExtractStepTypeArguments_ValidStep_ReturnsTuple()
    {
        // Act
        var (tIn, tOut) = ReflectionHelpers.ExtractStepTypeArguments<TestStep>();

        // Assert
        tIn.Should().Be(typeof(string));
        tOut.Should().Be(typeof(int));
    }

    [Theory]
    public async Task ExtractStepTypeArguments_NonStepType_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var act = () => ReflectionHelpers.ExtractStepTypeArguments<NotAStep>();
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

    private class TestStep : Step<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class NotAStep { }

    #endregion
}
