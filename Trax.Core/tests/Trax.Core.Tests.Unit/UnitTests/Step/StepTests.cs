using Trax.Core.Step;
using Trax.Core.Tests.Unit.Utils;
using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Trax.Core.Tests.Unit.UnitTests.Step;

public class StepTests : TestSetup
{
    [Theory]
    public async Task TestValidStepRun()
    {
        // Arrange
        var input = 1;
        var step = new TestStep();

        // Act
        var result = await step.Run(1);

        // Assert
        result.Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidStepRun()
    {
        // Arrange
        var input = 1;
        var step = new TestExceptionStep();

        // Act
        Assert.ThrowsAsync<NotImplementedException>(async () => await step.Run(input));
    }

    [Theory]
    public async Task TestValidRailwayStep()
    {
        // Arrange
        var input = 1;
        var step = new TestStep();

        // Act
        var result = await step.RailwayStep(input, UnitWorkflow.Create());

        // Assert
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidRailwayStep()
    {
        // Arrange
        var testException = new Exception("Test exception");
        var step = new TestStep();

        // Act
        var result = await step.RailwayStep(testException, UnitWorkflow.Create());

        // Assert
        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Should().Be(testException);
    }

    public class TestStep : Step<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }

    public class TestExceptionStep : Step<int, string>
    {
        public override async Task<string> Run(int input) => throw new NotImplementedException();
    }
}
