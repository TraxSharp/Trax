using FluentAssertions;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class ExtractTupleTests : TestSetup
{
    [Theory]
    public async Task TestExtractTuple()
    {
        // Arrange
        var input = 1;

        var inputObject = new object();
        var train = new TestTrain();
        var monad = train.Activate(input, "hello", false, 'c', inputObject);

        var inputTuple = typeof(ValueTuple<int, string, bool, char, object>);

        // Act
        var result = (ValueTuple<int, string, bool, char, object>)monad.ExtractTuple(inputTuple);

        // Assert
        result.Should().NotBeNull();
        result.Item1.Should().Be(1);
        result.Item2.Should().Be("hello");
        result.Item3.Should().Be(false);
        result.Item4.Should().Be('c');
        result.Item5.Should().Be(inputObject);
    }

    [Theory]
    public async Task TestInvalidExtractTuple()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();
        var monad = train.Activate(input);

        var inputTuple = typeof(ValueTuple<int>);

        // Act
        Assert.Throws<TrainException>(() => monad.ExtractTuple(inputTuple));
    }

    private class TestTrain : Train<int, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(int input) =>
            Activate(input).Resolve();
    }
}
