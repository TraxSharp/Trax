using Trax.Core.Step;
using Trax.Core.Train;
using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Trax.Core.Tests.Unit.UnitTests.Workflow;

public class ShortCircuitTests : TestSetup
{
    [Theory]
    public async Task TestShortCircuitChain()
    {
        // Arrange
        var input = 1;
        var testStep = new TestStep();
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.ShortCircuitChain<TestStep, string, bool>(
            testStep,
            inputString,
            out var returnValue
        );

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().BeTrue();
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
    }

    [Theory]
    public async Task TestShortCircuitChainTupleOutput()
    {
        // Arrange
        var input = 1;
        var testStep = new TestTupleOutputStep();
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.ShortCircuitChain<TestTupleOutputStep, string, (bool, char)>(
            testStep,
            inputString,
            out var returnValue
        );

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be((inputString.Equals("hello"), inputString.First()));
        workflow.Memory.Should().ContainValue(inputString.Equals("hello"));
        workflow.Memory.Should().ContainValue(inputString.First());
    }

    [Theory]
    public async Task TestShortCircuitChainFailure()
    {
        // Arrange
        var input = 1;
        var testStep = new TestExceptionStep();
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.ShortCircuitChain<TestExceptionStep, string, bool>(
            testStep,
            inputString,
            out var returnValue
        );

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        workflow.Memory.Should().NotContainValue(inputString.Equals("hello"));
    }

    [Theory]
    public async Task TestShortCircuitOneType()
    {
        // Arrange
        var input = 1;
        var inputString = "hello";
        var workflow = new TestWorkflow().Activate(input, inputString);

        // Act
        workflow.ShortCircuit<TestStepStringOutput>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
        workflow.Memory.Should().ContainValue("helloworld");
    }

    [Theory]
    public async Task TestInvalidShortCircuitOneType()
    {
        // Arrange
        var input = 1;
        var workflow = new TestWorkflow().Activate(input);

        // Act
        workflow.ShortCircuit<TestStepStringOutput>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().NotBeNull();
    }

    [Theory]
    public async Task TestValidOptionStepTest()
    {
        // Arrange
        var input = new Option<object>();
        var workflow = new TestWorkflowOption().Activate(input);

        // Act
        workflow.ShortCircuit<TestOptionStepTest>();

        // Assert
        workflow.Memory.Should().NotBeNull();
        workflow.Exception.Should().BeNull();
    }

    private class TestExceptionStep : Step<string, bool>
    {
        public override Task<bool> Run(string input) => throw new NotImplementedException();
    }

    private class TestTupleOutputStep : Step<string, (bool, char)>
    {
        public override async Task<(bool, char)> Run(string input) =>
            (input.Equals("hello"), input.First());
    }

    private class TestStep : Step<string, bool>
    {
        public override async Task<bool> Run(string input) => input.Equals("hello");
    }

    private class TestStepStringOutput : Step<string, string>
    {
        public override async Task<string> Run(string input) => input + "world";
    }

    private class TestWorkflow : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    public class TestOptionStepTest : Step<Option<object>, string>
    {
        public override async Task<string> Run(Option<object> input)
        {
            return "hello world";
        }
    }

    private class TestWorkflowOption : Train<Option<object>, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(Option<object> input) =>
            throw new NotImplementedException();
    }
}
