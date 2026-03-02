using FluentAssertions;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class TrainTests
{
    [Theory]
    public async Task TestUnitTrain()
    {
        // Arrange
        var train = new UnitTrain();

        // Act
        var result = await train.Run(LanguageExt.Unit.Default);

        // Assert
        result.Should().Be(LanguageExt.Unit.Default);
    }

    [Theory]
    public async Task TestInvalidTrain()
    {
        // Arrange
        var train = new NotImplementedTrain();

        // Act
        Assert.ThrowsAsync<NotImplementedException>(async () =>
            await train.Run(LanguageExt.Unit.Default)
        );
    }

    private class UnitTrain : Train<LanguageExt.Unit, LanguageExt.Unit>
    {
        protected override async Task<Either<Exception, LanguageExt.Unit>> RunInternal(
            LanguageExt.Unit input
        ) => Activate(input).Resolve();
    }

    private class NotImplementedTrain : Train<LanguageExt.Unit, LanguageExt.Unit>
    {
        protected override async Task<Either<Exception, LanguageExt.Unit>> RunInternal(
            LanguageExt.Unit input
        ) => new NotImplementedException();
    }
}
