using FluentAssertions;
using LanguageExt;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class InitializeJunctionTests : TestSetup
{
    [Theory]
    public async Task TestInitializeJunction()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var junction = monad.InitializeJunction<TestValidJunction, int, string>();

        // Assert
        junction.Should().NotBeNull();

        var result = await junction!.Run(input);
        result.Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidInitializeJunction()
    {
        // Arrange
        var train = new TestTrain();
        var monad = train.Activate(1);

        // Act
        var junction = monad.InitializeJunction<TestInvalidJunction, int, string>();

        // Assert
        monad.Exception.Should().NotBeNull();
    }

    private class TestTrain : Train<int, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(int input) =>
            Activate(input).Resolve();
    }

    private class TestValidJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input)
        {
            return input.ToString();
        }
    }

#pragma warning disable CS9113 // Parameter is unread - intentionally invalid junction for testing
    private class TestInvalidJunction(int _intInput, string _stringInput) { }
#pragma warning restore CS9113
}
