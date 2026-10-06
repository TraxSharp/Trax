using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ShortCircuitTests : TestSetup
{
    [Theory]
    public async Task TestShortCircuitChain()
    {
        // Arrange
        var input = 1;
        var testJunction = new TestJunction();
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        var (_, returnValue) = await train.ShortCircuitJunction<TestJunction, string, bool>(
            testJunction,
            inputString
        );

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().BeTrue();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    [Theory]
    public async Task TestShortCircuitChainTupleOutput()
    {
        // Arrange
        var input = 1;
        var testJunction = new TestTupleOutputJunction();
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        var (_, returnValue) = await train.ShortCircuitJunction<
            TestTupleOutputJunction,
            string,
            (bool, char)
        >(testJunction, inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be((inputString.Equals("hello"), inputString.First()));
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
        train.Memory.Should().ContainValue(inputString.First());
    }

    [Theory]
    public async Task TestShortCircuitChainFailure()
    {
        // Arrange
        var input = 1;
        var testJunction = new TestExceptionJunction();
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        var (_, returnValue) = await train.ShortCircuitJunction<
            TestExceptionJunction,
            string,
            bool
        >(testJunction, inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        train.Memory.Should().NotContainValue(inputString.Equals("hello"));
    }

    [Theory]
    public async Task TestShortCircuitOneType()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        await train.ShortCircuit<TestJunctionStringOutput>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue("helloworld");
    }

    [Theory]
    public async Task TestInvalidShortCircuitOneType()
    {
        // Arrange
        var input = 1;
        var train = new TestTrain().Activate(input);

        // Act
        await train.ShortCircuit<TestJunctionStringOutput>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    [Theory]
    public async Task TestValidStructInputJunctionTest()
    {
        // Arrange
        var input = new Maybe();
        var train = new TestTrainOption().Activate(input);

        // Act
        await train.ShortCircuit<TestOptionJunctionTest>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    private class TestExceptionJunction : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => throw new NotImplementedException();
    }

    private class TestTupleOutputJunction : Junction<string, (bool, char)>
    {
        public override async Task<(bool, char)> Run(string input) =>
            (input.Equals("hello"), input.First());
    }

    private class TestJunction : Junction<string, bool>
    {
        public override async Task<bool> Run(string input) => input.Equals("hello");
    }

    private class TestJunctionStringOutput : Junction<string, string>
    {
        public override async Task<string> Run(string input) => input + "world";
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }

    // A struct input whose default value is valid.
    public readonly record struct Maybe(object? Value);

    public class TestOptionJunctionTest : Junction<Maybe, string>
    {
        public override async Task<string> Run(Maybe input)
        {
            return "hello world";
        }
    }

    private class TestTrainOption : Train<Maybe, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }
}
