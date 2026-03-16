using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ChainTests : TestSetup
{
    // Chain<TJunction, TIn, TOut>(TJunction, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypes()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestJunction, string, bool>(
            new TestJunction(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be(stringInput.Equals("hello"));
        train.Memory.Should().ContainValue(stringInput.Equals("hello"));
        train.Exception.Should().BeNull();
    }

    // Chain<TJunction, TIn, TOut>(TJunction, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesPreviousJunctionException()
    {
        // Arrange
        var trainInput = 1;
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestJunction, string, bool>(
            new TestJunction(),
            new Exception(),
            out var returnValue
        );

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<Exception>();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TJunction, TIn, TOut>(TJunction, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesJunctionException()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestExceptionJunction, string, bool>(
            new TestExceptionJunction(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TJunction, TIn, TOut>(TJunction, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTupleOutput()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestTupleOutputJunction, string, (bool, char)>(
            new TestTupleOutputJunction(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be((stringInput.Equals("hello"), stringInput.First()));
        train.Memory.Should().ContainValue(stringInput.Equals("hello"));
        train.Memory.Should().ContainValue(stringInput.First());
        train.Exception.Should().BeNull();
    }

    // Chain<TJunction, TIn, TOut>(TJunction)
    [Theory]
    public async Task TestChainThreeTypesOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestJunction, string, bool>(new TestJunction());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TJunction, TIn, TOut>(TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTwoInputs()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestJunction, string, bool>(inputString, out var returnValue);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().BeTrue();
    }

    // Chain<TJunction, TIn, TOut>()
    [Theory]
    public async Task TestChainThreeTypesNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestJunction, string, bool>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TJunction>()
    [Theory]
    public async Task TestIChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testJunction = (ITestJunction)new TestJunction();
        var train = new TestTrain().Activate(input, inputString).AddServices(testJunction);

        // Act
        train.IChain<ITestJunction>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TJunction>()
    [Theory]
    public async Task TestInvalidIChainOneTypeNoInputNotInterface()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.IChain<TestJunction>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TJunction>()
    [Theory]
    public async Task TestChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestJunction>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TJunction>(TJunction)
    [Theory]
    public async Task TestChainOneTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testJunction = new TestJunction();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestJunction>(testJunction);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TJunction, TIn>(TJunction, TIn)
    [Theory]
    public async Task TestChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testJunction = new TestUnitJunction();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitJunction, string>(testJunction, inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TJunction, TIn>(TJunction, TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testJunction = new TestUnitJunction();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitJunction, string>(testJunction, new Exception());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TJunction, TIn>(TJunction)
    [Theory]
    public async Task TestChainTwoTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testJunction = new TestUnitJunction();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitJunction, string>(testJunction);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TJunction, TIn>(TIn)
    [Theory]
    public async Task TestChainTwoTypeOnePreviousJunctionInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestUnitJunction, string>(inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TJunction, TIn>(TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeOnePreviousJunctionInput()
    {
        // Arrange
        var input = 1;
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestUnitJunction, string>(new Exception());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TJunction, TIn>()
    [Theory]
    public async Task TestChainTwoTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitJunction, string>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    private class TestTupleOutputJunction : Junction<string, (bool, char)>
    {
        public override async Task<(bool, char)> Run(string input) =>
            (input.Equals("hello"), input.First());
    }

    private class TestExceptionJunction : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => throw new NotImplementedException();
    }

    private interface ITestUnitJunction : IJunction<string, LanguageExt.Unit>;

    private class TestUnitJunction : Junction<string, LanguageExt.Unit>, ITestUnitJunction
    {
        public override async Task<LanguageExt.Unit> Run(string input) => LanguageExt.Unit.Default;
    }

    private interface ITestJunction : IJunction<string, bool> { }

    private class TestJunction : Junction<string, bool>, ITestJunction
    {
        public override async Task<bool> Run(string input) => input.Equals("hello");
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
