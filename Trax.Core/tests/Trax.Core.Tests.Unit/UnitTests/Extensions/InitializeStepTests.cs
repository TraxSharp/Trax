using FluentAssertions;
using LanguageExt;
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class InitializeStepTests : TestSetup
{
    [Theory]
    public async Task TestInitializeStep()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var step = monad.InitializeStep<TestValidStep, int, string>();

        // Assert
        step.Should().NotBeNull();

        var result = await step!.Run(input);
        result.Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidInitializeStep()
    {
        // Arrange
        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var step = monad.InitializeStep<TestInvalidStep, int, string>();

        // Assert
        monad.Exception.Should().NotBeNull();
    }

    private class TestTrain : Train<int, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(int input) =>
            Activate(input).Resolve();
    }

    private class TestValidStep : Step<int, string>
    {
        public override async Task<string> Run(int input)
        {
            return input.ToString();
        }
    }

    private class TestInvalidStep(int _intInput, string _stringInput) { }
}
