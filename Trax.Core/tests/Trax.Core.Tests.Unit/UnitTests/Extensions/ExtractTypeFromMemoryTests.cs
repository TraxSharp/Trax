using FluentAssertions;
using LanguageExt;
using Trax.Core.Extensions;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class ExtractTypeFromMemoryTests : TestSetup
{
    [Theory]
    public async Task TestExtractTypeFromMemory()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(input);

        // Act
        var result = monad.ExtractTypeFromMemory<int, int, string>();

        // Assert
        result.Should().Be(input);
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestTupleExtractTypeFromMemory()
    {
        // Arrange
        var input = 1;
        var tupleInput = ("hello", false);

        var train = new TestTrain();
        var monad = train.Activate(input, tupleInput);

        // Act
        var result = monad.ExtractTypeFromMemory<(string, bool), int, string>();

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(tupleInput);
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestTupleExtractTypeFromMemoryTypeInput()
    {
        // Arrange
        var input = 1;
        var tupleInput = ("hello", false);

        var train = new TestTrain();
        var monad = train.Activate(input, tupleInput);

        // Act
        var result = ((string, bool)?)monad.ExtractTypeFromMemory(typeof((string, bool)));

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(tupleInput);
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestInvalidTupleExtractTypeFromMemoryTypeInput()
    {
        // Arrange
        var input = 1;
        var tupleInput = ValueTuple.Create(1);

        var train = new TestTrain();
        var monad = train.Activate(input, tupleInput);

        // Act
        var result = (ValueTuple<int>?)monad.ExtractTypeFromMemory(typeof(ValueTuple<int>));

        // Assert
        monad.Exception.Should().NotBeNull();
        result.Should().BeNull();
    }

    [Theory]
    public async Task TestInvalidTupleExtractTypeFromMemory()
    {
        // Arrange
        var input = 1;
        var tupleInput = ValueTuple.Create(1);

        var train = new TestTrain();
        var monad = train.Activate(input, tupleInput);

        // Act
        var result = monad.ExtractTypeFromMemory<ValueTuple<int>?, int, string>();

        // Assert
        monad.Exception.Should().NotBeNull();
        result.Should().BeNull();
    }

    [Theory]
    public async Task TestValidExtractTypesFromMemory()
    {
        // Arrange
        var input = 1;
        var tupleInput = ("hello", false);

        var train = new TestTrain();
        var monad = train.Activate(input, tupleInput);

        List<Type> typesToExtract = [typeof(bool), typeof(string), typeof(int)];

        // Act
        var result = monad.ExtractTypesFromMemory(typesToExtract);

        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        result.Should().Contain(false);
        result.Should().Contain("hello");
        result.Should().Contain(1);
    }

    [Theory]
    public async Task TestInvalidExtractTypesFromMemory()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(input);

        List<Type> typesToExtract = [typeof(bool), typeof(string), typeof(int)];

        // Act
        var result = monad.ExtractTypesFromMemory(typesToExtract);

        result.Should().NotBeNull();
        result.Length.Should().Be(3);
        monad.Exception.Should().NotBeNull();
    }

    private class TestTrain : Train<int, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(int input) =>
            Activate(input).Resolve();
    }
}
