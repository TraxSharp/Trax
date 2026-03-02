using FluentAssertions;
using LanguageExt;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class CancellationTokenTests : TestSetup
{
    [Theory]
    public async Task Run_WithCancellationToken_SetsPropertyOnTrain()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var train = new TokenCapturingTrain();

        // Act
        await train.Run("input", cts.Token);

        // Assert
        train.CapturedToken.Should().Be(cts.Token);
    }

    [Theory]
    public async Task Run_WithoutCancellationToken_DefaultsToNone()
    {
        // Arrange
        var train = new TokenCapturingTrain();

        // Act
        await train.Run("input");

        // Assert
        train.CapturedToken.Should().Be(CancellationToken.None);
    }

    [Theory]
    public async Task Step_CancellationToken_IsSetFromTrain()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step = new TokenCapturingStep();
        var train = new SingleStepTrain(step);

        // Act
        await train.Run("input", cts.Token);

        // Assert
        step.CapturedToken.Should().Be(cts.Token);
    }

    [Theory]
    public async Task Step_CancellationToken_IsNone_WhenTrainCalledWithoutToken()
    {
        // Arrange
        var step = new TokenCapturingStep();
        var train = new SingleStepTrain(step);

        // Act
        await train.Run("input");

        // Assert
        step.CapturedToken.Should().Be(CancellationToken.None);
    }

    [Theory]
    public async Task CancelledToken_BeforeStepExecution_ThrowsAndPreventsExecution()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new CountingStep();
        var train = new SingleStepTrain(step);

        // Act & Assert — Chain uses Task.Run().Result which may wrap the
        // OperationCanceledException; verify the step was never executed
        Exception? caught = null;
        try
        {
            await train.Run("input", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        HasCancellationException(caught!).Should().BeTrue();
        step.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task CancelledToken_BetweenSteps_SkipsSubsequentSteps()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step1 = new CancellationTriggerStep(cts);
        var step2 = new CountingStep();
        var train = new TwoStepTrain(step1, step2);

        // Act & Assert — step1 cancels the token; step2 should not run
        Exception? caught = null;
        try
        {
            await train.Run("input", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        HasCancellationException(caught!).Should().BeTrue();
        step1.ExecutionCount.Should().Be(1);
        step2.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task CancelledToken_DuringStep_PropagatesAsException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step = new SlowStep();
        var train = new SingleStepTrain(step);

        // Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        Exception? caught = null;
        try
        {
            await train.Run("input", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        HasCancellationException(caught!).Should().BeTrue();
    }

    [Theory]
    public async Task CancelledToken_DoesNotWrap_InTrainExceptionData()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new TokenCapturingStep();
        var train = new SingleStepTrain(step);

        // Act — the step never runs because the token is already cancelled
        Exception? caught = null;
        try
        {
            await train.Run("input", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert
        caught.Should().NotBeNull();
        step.ExceptionData.Should().BeNull();
    }

    /// <summary>
    /// Checks whether the exception or any of its inner exceptions is an OperationCanceledException.
    /// Chain uses Task.Run().Result which wraps exceptions in AggregateException/TargetInvocationException.
    /// </summary>
    private static bool HasCancellationException(Exception ex)
    {
        if (ex is OperationCanceledException)
            return true;

        if (ex is AggregateException agg)
            return agg.Flatten().InnerExceptions.Any(e => e is OperationCanceledException);

        return ex.InnerException != null && HasCancellationException(ex.InnerException);
    }

    [Theory]
    public async Task MultipleSteps_AllReceiveToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step1 = new TokenCapturingStep();
        var step2 = new TokenCapturingPassthroughStep();
        var train = new TwoStepTrain(step1, step2);

        // Act
        await train.Run("input", cts.Token);

        // Assert
        step1.CapturedToken.Should().Be(cts.Token);
        step2.CapturedToken.Should().Be(cts.Token);
    }

    #region Test Helpers

    private class TokenCapturingTrain : Train<string, string>
    {
        public CancellationToken CapturedToken { get; private set; }

        protected override Task<Either<Exception, string>> RunInternal(string input)
        {
            CapturedToken = CancellationToken;
            return Task.FromResult<Either<Exception, string>>(input);
        }
    }

    private class TokenCapturingStep : Step<string, string>
    {
        public CancellationToken CapturedToken { get; private set; }

        public override Task<string> Run(string input)
        {
            CapturedToken = CancellationToken;
            return Task.FromResult(input);
        }
    }

    private class TokenCapturingPassthroughStep : Step<string, string>
    {
        public CancellationToken CapturedToken { get; private set; }

        public override Task<string> Run(string input)
        {
            CapturedToken = CancellationToken;
            return Task.FromResult(input);
        }
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

    private class CancellationTriggerStep : Step<string, string>
    {
        private readonly CancellationTokenSource _cts;
        public int ExecutionCount { get; private set; }

        public CancellationTriggerStep(CancellationTokenSource cts) => _cts = cts;

        public override Task<string> Run(string input)
        {
            ExecutionCount++;
            _cts.Cancel();
            return Task.FromResult(input);
        }
    }

    private class SlowStep : Step<string, string>
    {
        public override async Task<string> Run(string input)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken);
            return input;
        }
    }

    private class SingleStepTrain : Train<string, string>
    {
        private readonly Step<string, string> _step;

        public SingleStepTrain(Step<string, string> step) => _step = step;

        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(_step).Resolve();
    }

    private class TwoStepTrain : Train<string, string>
    {
        private readonly Step<string, string> _step1;
        private readonly Step<string, string> _step2;

        public TwoStepTrain(Step<string, string> step1, Step<string, string> step2)
        {
            _step1 = step1;
            _step2 = step2;
        }

        protected override async Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(_step1).Chain(_step2).Resolve();
    }

    #endregion
}
