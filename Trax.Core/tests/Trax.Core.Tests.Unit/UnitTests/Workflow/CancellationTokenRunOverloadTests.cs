using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Workflow;

public class CancellationTokenRunOverloadTests : TestSetup
{
    [Theory]
    public async Task Run_InputAndToken_ReturnsResult()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var workflow = new SimpleWorkflow();

        // Act
        var result = await workflow.Run("hello", cts.Token);

        // Assert
        result.Should().Be("hello_processed");
    }

    [Theory]
    public async Task RunEither_InputAndToken_ReturnsRight()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var workflow = new SimpleWorkflow();

        // Act
        workflow.CancellationToken = cts.Token;
        var result = await workflow.RunEither("hello");

        // Assert
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be("hello_processed");
    }

    [Theory]
    public async Task RunEither_InputAndToken_ReturnsLeft_OnException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var workflow = new FailingWorkflow();

        // Act
        workflow.CancellationToken = cts.Token;
        var result = await workflow.RunEither("hello");

        // Assert
        result.IsLeft.Should().BeTrue();
    }

    [Theory]
    public async Task Run_WithToken_StoresTokenBeforeRunInternal()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var workflow = new TokenCapturingWorkflow();

        // Act
        await workflow.Run("hello", cts.Token);

        // Assert
        workflow.TokenDuringExecution.Should().Be(cts.Token);
    }

    [Theory]
    public async Task Run_WithCancelledToken_DoesNotExecuteRunInternal()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new CountingStep();
        var workflow = new StepWorkflow(step);

        // Act & Assert — Chain uses Task.Run().Result which wraps
        // OperationCanceledException; verify the step was never executed
        Exception? caught = null;
        try
        {
            await workflow.Run("hello", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        step.ExecutionCount.Should().Be(0);
    }

    #region Test Helpers

    private class SimpleWorkflow : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(new ProcessStep()).Resolve();
    }

    private class FailingWorkflow : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(new FailStep()).Resolve();
    }

    private class TokenCapturingWorkflow : Train<string, string>
    {
        public CancellationToken TokenDuringExecution { get; private set; }

        protected override Task<Either<Exception, string>> RunInternal(string input)
        {
            TokenDuringExecution = CancellationToken;
            return Task.FromResult<Either<Exception, string>>(input);
        }
    }

    private class StepWorkflow : Train<string, string>
    {
        private readonly Step<string, string> _step;

        public StepWorkflow(Step<string, string> step) => _step = step;

        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(_step).Resolve();
    }

    private class ProcessStep : Step<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input + "_processed");
    }

    private class FailStep : Step<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("test failure");
    }

    private class CountingStep : Step<string, string>
    {
        public int ExecutionCount { get; private set; }

        public override Task<string> Run(string input)
        {
            ExecutionCount++;
            return Task.FromResult(input);
        }
    }

    #endregion
}
