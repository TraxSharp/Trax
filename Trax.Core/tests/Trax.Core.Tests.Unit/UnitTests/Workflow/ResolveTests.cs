using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Train;
using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Trax.Core.Tests.Unit.UnitTests.Workflow;

public class ResolveTests : TestSetup
{
    [Theory]
    public async Task TestResolvePrimitive()
    {
        // Arrange
        var input = 1;
        var workflow = new TestWorkflow().Activate(input);

        // Act
        var result = workflow.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(1);
    }

    [Theory]
    public async Task TestResolveObject()
    {
        // Arrange
        var input = new object();
        var workflow = new TestObjectWorkflow().Activate(input);

        // Act
        var result = workflow.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input);
    }

    [Theory]
    public async Task TestResolveTuple()
    {
        // Arrange
        var intInput = 1;
        var stringInput = "string";
        var workflow = new TestTupleWorkflow().Activate(
            LanguageExt.Unit.Default,
            intInput,
            stringInput
        );

        // Act
        var result = workflow.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be((intInput, stringInput));
    }

    [Theory]
    public async Task TestResolveShortCircuitValueSet()
    {
        // Arrange
        var input = 1;
        var workflow = new TestStringWorkflow()
            .Activate(input)
            .ShortCircuit<TestShortCircuitStep>();

        // Act
        var result = workflow.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidResolve()
    {
        // Arrange
        var input = 1;
        var workflow = new TestStringWorkflow().Activate(input);

        // Act
        var result = workflow.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Should().BeOfType<WorkflowException>();
    }

    private class TestWorkflow : Train<int, int>
    {
        protected override Task<Either<Exception, int>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    private class TestStringWorkflow : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    private class TestObjectWorkflow : Train<object, object>
    {
        protected override Task<Either<Exception, object>> RunInternal(object input) =>
            throw new NotImplementedException();
    }

    private class TestTupleWorkflow : Train<LanguageExt.Unit, (int, string)>
    {
        protected override Task<Either<Exception, (int, string)>> RunInternal(
            LanguageExt.Unit input
        ) => throw new NotImplementedException();
    }

    private class TestShortCircuitStep : Step<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }
}
