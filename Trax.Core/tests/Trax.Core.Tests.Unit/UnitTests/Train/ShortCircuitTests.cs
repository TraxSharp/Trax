using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
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
    public async Task TestValidOptionJunctionTest()
    {
        // Arrange
        var input = new Option<object>();
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
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    public class TestOptionJunctionTest : Junction<Option<object>, string>
    {
        public override async Task<string> Run(Option<object> input)
        {
            return "hello world";
        }
    }

    private class TestTrainOption : Train<Option<object>, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(Option<object> input) =>
            throw new NotImplementedException();
    }
}
