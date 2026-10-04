using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
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
        var result = await train.Run(Trax.Core.Functional.Unit.Default);

        // Assert
        result.Should().Be(Trax.Core.Functional.Unit.Default);
    }

    [Theory]
    public async Task TestInvalidTrain()
    {
        // Arrange
        var train = new NotImplementedTrain();

        // Act
        await Assert.ThrowsAsync<NotImplementedException>(async () =>
            await train.Run(Trax.Core.Functional.Unit.Default)
        );
    }

    private class UnitTrain : Train<Trax.Core.Functional.Unit, Trax.Core.Functional.Unit>
    {
        protected override async Task<Either<Exception, Trax.Core.Functional.Unit>> Junctions() =>
            Resolve();
    }

    private class NotImplementedTrain : Train<Trax.Core.Functional.Unit, Trax.Core.Functional.Unit>
    {
        protected override async Task<Either<Exception, Trax.Core.Functional.Unit>> Junctions() =>
            new NotImplementedException();
    }
}
