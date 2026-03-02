using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ChainTests : TestSetup
{
    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypes()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestStep, string, bool>(new TestStep(), stringInput, out var returnValue);

        // Assert
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be(stringInput.Equals("hello"));
        train.Memory.Should().ContainValue(stringInput.Equals("hello"));
        train.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesPreviousStepException()
    {
        // Arrange
        var trainInput = 1;
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestStep, string, bool>(new TestStep(), new Exception(), out var returnValue);

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<Exception>();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesStepException()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestExceptionStep, string, bool>(
            new TestExceptionStep(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTupleOutput()
    {
        // Arrange
        var trainInput = 1;
        var stringInput = "hello";
        var train = new TestTrain().Activate(trainInput);

        // Act
        train.Chain<TestTupleOutputStep, string, (bool, char)>(
            new TestTupleOutputStep(),
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

    // Chain<TStep, TIn, TOut>(TStep)
    [Theory]
    public async Task TestChainThreeTypesOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestStep, string, bool>(new TestStep());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep, TIn, TOut>(TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTwoInputs()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestStep, string, bool>(inputString, out var returnValue);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().BeTrue();
    }

    // Chain<TStep, TIn, TOut>()
    [Theory]
    public async Task TestChainThreeTypesNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestStep, string, bool>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TStep>()
    [Theory]
    public async Task TestIChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = (ITestStep)new TestStep();
        var train = new TestTrain().Activate(input, inputString).AddServices(testStep);

        // Act
        train.IChain<ITestStep>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TStep>()
    [Theory]
    public async Task TestInvalidIChainOneTypeNoInputNotInterface()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.IChain<TestStep>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TStep>()
    [Theory]
    public async Task TestChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestStep>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep>(TStep)
    [Theory]
    public async Task TestChainOneTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestStep();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestStep>(testStep);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
        train.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep, TIn>(TStep, TIn)
    [Theory]
    public async Task TestChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitStep, string>(testStep, inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TStep, TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitStep, string>(testStep, new Exception());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn>(TStep)
    [Theory]
    public async Task TestChainTwoTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitStep, string>(testStep);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TIn)
    [Theory]
    public async Task TestChainTwoTypeOnePreviousStepInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestUnitStep, string>(inputString);

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeOnePreviousStepInput()
    {
        // Arrange
        var input = 1;
        var train = new TestTrain().Activate(input);

        // Act
        train.Chain<TestUnitStep, string>(new Exception());

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn>()
    [Theory]
    public async Task TestChainTwoTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var train = new TestTrain().Activate(input, inputString);

        // Act
        train.Chain<TestUnitStep, string>();

        // Assert
        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    private class TestTupleOutputStep : Step<string, (bool, char)>
    {
        public override async Task<(bool, char)> Run(string input) =>
            (input.Equals("hello"), input.First());
    }

    private class TestExceptionStep : Step<string, bool>
    {
        public override Task<bool> Run(string input) => throw new NotImplementedException();
    }

    private interface ITestUnitStep : IStep<string, LanguageExt.Unit>;

    private class TestUnitStep : Step<string, LanguageExt.Unit>, ITestUnitStep
    {
        public override async Task<LanguageExt.Unit> Run(string input) => LanguageExt.Unit.Default;
    }

    private interface ITestStep : IStep<string, bool> { }

    private class TestStep : Step<string, bool>, ITestStep
    {
        public override async Task<bool> Run(string input) => input.Equals("hello");
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
