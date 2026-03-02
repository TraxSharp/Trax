using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class CancellationTokenRunOverloadTests : TestSetup
{
    [Theory]
    public async Task Run_InputAndToken_ReturnsResult()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var train = new SimpleTrain();

        // Act
        var result = await train.Run("hello", cts.Token);

        // Assert
        result.Should().Be("hello_processed");
    }

    [Theory]
    public async Task RunEither_InputAndToken_ReturnsRight()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var train = new SimpleTrain();

        // Act
        train.CancellationToken = cts.Token;
        var result = await train.RunEither("hello");

        // Assert
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be("hello_processed");
    }

    [Theory]
    public async Task RunEither_InputAndToken_ReturnsLeft_OnException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var train = new FailingTrain();

        // Act
        train.CancellationToken = cts.Token;
        var result = await train.RunEither("hello");

        // Assert
        result.IsLeft.Should().BeTrue();
    }

    [Theory]
    public async Task Run_WithToken_StoresTokenBeforeRunInternal()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var train = new TokenCapturingTrain();

        // Act
        await train.Run("hello", cts.Token);

        // Assert
        train.TokenDuringExecution.Should().Be(cts.Token);
    }

    [Theory]
    public async Task Run_WithCancelledToken_DoesNotExecuteRunInternal()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new CountingStep();
        var train = new StepTrain(step);

        // Act & Assert — Chain uses Task.Run().Result which wraps
        // OperationCanceledException; verify the step was never executed
        Exception? caught = null;
        try
        {
            await train.Run("hello", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        step.ExecutionCount.Should().Be(0);
    }

    #region Test Helpers

    private class SimpleTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(new ProcessStep()).Resolve();
    }

    private class FailingTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(new FailStep()).Resolve();
    }

    private class TokenCapturingTrain : Train<string, string>
    {
        public CancellationToken TokenDuringExecution { get; private set; }

        protected override Task<Either<Exception, string>> RunInternal(string input)
        {
            TokenDuringExecution = CancellationToken;
            return Task.FromResult<Either<Exception, string>>(input);
        }
    }

    private class StepTrain : Train<string, string>
    {
        private readonly Step<string, string> _step;

        public StepTrain(Step<string, string> step) => _step = step;

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
