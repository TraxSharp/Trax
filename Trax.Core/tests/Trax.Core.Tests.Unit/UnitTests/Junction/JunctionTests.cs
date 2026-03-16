using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Tests.Unit.Utils;

namespace Trax.Core.Tests.Unit.UnitTests.Step;

public class JunctionTests : TestSetup
{
    [Theory]
    public async Task TestValidStepRun()
    {
        // Arrange
        var input = 1;
        var junction = new TestJunction();

        // Act
        var result = await junction.Run(1);

        // Assert
        result.Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidStepRun()
    {
        // Arrange
        var input = 1;
        var junction = new TestExceptionJunction();

        // Act
        Assert.ThrowsAsync<NotImplementedException>(async () => await junction.Run(input));
    }

    [Theory]
    public async Task TestValidRailwayJunction()
    {
        // Arrange
        var input = 1;
        var junction = new TestJunction();

        // Act
        var result = await junction.RailwayJunction(input, UnitTrain.Create());

        // Assert
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidRailwayJunction()
    {
        // Arrange
        var testException = new Exception("Test exception");
        var junction = new TestJunction();

        // Act
        var result = await junction.RailwayJunction(testException, UnitTrain.Create());

        // Assert
        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Should().Be(testException);
    }

    public class TestJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }

    public class TestExceptionJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input) => throw new NotImplementedException();
    }
}
