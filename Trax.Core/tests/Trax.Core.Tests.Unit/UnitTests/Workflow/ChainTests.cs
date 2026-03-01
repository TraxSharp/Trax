using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Workflow;

public class ChainTests : TestSetup
{
    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypes()
    {
        // Arrange
        var workflowInput = 1;
        var stringInput = "hello";
        var workflow = new TestWorkflow().Activate(workflowInput);

        // Act
        workflow.Chain<TestStep, string, bool>(new TestStep(), stringInput, out var returnValue);

        // Assert
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be(stringInput.Equals("hello"));
        workflow.Memory.Should().ContainValue(stringInput.Equals("hello"));
        workflow.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesPreviousStepException()
    {
        // Arrange
        var workflowInput = 1;
        var workflow = new TestWorkflow().Activate(workflowInput);

        // Act
        workflow.Chain<TestStep, string, bool>(
            new TestStep(),
            new Exception(),
            out var returnValue
        );

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<Exception>();
        workflow.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesStepException()
    {
        // Arrange
        var workflowInput = 1;
        var stringInput = "hello";
        var workflow = new TestWorkflow().Activate(workflowInput);

        // Act
        workflow.Chain<TestExceptionStep, string, bool>(
            new TestExceptionStep(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        workflow.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep, TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTupleOutput()
    {
        // Arrange
        var workflowInput = 1;
        var stringInput = "hello";
        var workflow = new TestWorkflow().Activate(workflowInput);

        // Act
        workflow.Chain<TestTupleOutputStep, string, (bool, char)>(
            new TestTupleOutputStep(),
            stringInput,
            out var returnValue
        );

        // Assert
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be((stringInput.Equals("hello"), stringInput.First()));
        workflow.Memory.Should().ContainValue(stringInput.Equals("hello"));
        workflow.Memory.Should().ContainValue(stringInput.First());
        workflow.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn, TOut>(TStep)
    [Theory]
    public async Task TestChainThreeTypesOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestStep, string, bool>(new TestStep());

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep, TIn, TOut>(TIn, TOut)
    [Theory]
    public async Task TestChainThreeTypesTwoInputs()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.Chain<TestStep, string, bool>(inputString, out var returnValue);

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
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
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestStep, string, bool>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TStep>()
    [Theory]
    public async Task TestIChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = (ITestStep)new TestStep();
        var workflow = new TestWorkflow().Activate(input, inputString).AddServices(testStep);

        // Act
        workflow.IChain<ITestStep>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // IChain<TStep>()
    [Theory]
    public async Task TestInvalidIChainOneTypeNoInputNotInterface()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.IChain<TestStep>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().NotBeNull();
    }

    // Chain<TStep>()
    [Theory]
    public async Task TestChainOneTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestStep>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep>(TStep)
    [Theory]
    public async Task TestChainOneTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestStep();
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestStep>(testStep);

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    // Chain<TStep, TIn>(TStep, TIn)
    [Theory]
    public async Task TestChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestUnitStep, string>(testStep, inputString);

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TStep, TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeTwoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestUnitStep, string>(testStep, new Exception());

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn>(TStep)
    [Theory]
    public async Task TestChainTwoTypeOneInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var testStep = new TestUnitStep();
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestUnitStep, string>(testStep);

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TIn)
    [Theory]
    public async Task TestChainTwoTypeOnePreviousStepInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.Chain<TestUnitStep, string>(inputString);

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
    }

    // Chain<TStep, TIn>(TIn)
    [Theory]
    public async Task TestInvalidChainTwoTypeOnePreviousStepInput()
    {
        // Arrange
        var input = 1;
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.Chain<TestUnitStep, string>(new Exception());

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().NotBeNull();
    }

    // Chain<TStep, TIn>()
    [Theory]
    public async Task TestChainTwoTypeNoInput()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.Chain<TestUnitStep, string>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
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

    private class TestWorkflow : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
