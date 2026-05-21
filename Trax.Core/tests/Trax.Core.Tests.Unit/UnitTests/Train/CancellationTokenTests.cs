using FluentAssertions;
using LanguageExt;
using Trax.Core.Junction;
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
    public async Task Junction_CancellationToken_IsSetFromTrain()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction = new TokenCapturingJunction();
        var train = new SingleJunctionTrain(junction);

        // Act
        await train.Run("input", cts.Token);

        // Assert
        junction.CapturedToken.Should().Be(cts.Token);
    }

    [Theory]
    public async Task Junction_CancellationToken_IsNone_WhenTrainCalledWithoutToken()
    {
        // Arrange
        var junction = new TokenCapturingJunction();
        var train = new SingleJunctionTrain(junction);

        // Act
        await train.Run("input");

        // Assert
        junction.CapturedToken.Should().Be(CancellationToken.None);
    }

    [Theory]
    public async Task CancelledToken_BeforeJunctionExecution_ThrowsAndPreventsExecution()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var junction = new CountingJunction();
        var train = new SingleJunctionTrain(junction);

        // Act & Assert — Chain uses Task.Run().Result which may wrap the
        // OperationCanceledException; verify the junction was never executed
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
        junction.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task CancelledToken_BetweenJunctions_SkipsSubsequentJunctions()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction1 = new CancellationTriggerJunction(cts);
        var junction2 = new CountingJunction();
        var train = new TwoJunctionTrain(junction1, junction2);

        // Act & Assert — junction1 cancels the token; junction2 should not run
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
        junction1.ExecutionCount.Should().Be(1);
        junction2.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task CancelledToken_DuringJunction_PropagatesAsException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction = new SlowJunction();
        var train = new SingleJunctionTrain(junction);

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
        var junction = new TokenCapturingJunction();
        var train = new SingleJunctionTrain(junction);

        // Act — the junction never runs because the token is already cancelled
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
        junction.ExceptionData.Should().BeNull();
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
    public async Task MultipleJunctions_AllReceiveToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction1 = new TokenCapturingJunction();
        var junction2 = new TokenCapturingPassthroughJunction();
        var train = new TwoJunctionTrain(junction1, junction2);

        // Act
        await train.Run("input", cts.Token);

        // Assert
        junction1.CapturedToken.Should().Be(cts.Token);
        junction2.CapturedToken.Should().Be(cts.Token);
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

    private class TokenCapturingJunction : Junction<string, string>
    {
        public CancellationToken CapturedToken { get; private set; }

        public override Task<string> Run(string input)
        {
            CapturedToken = CancellationToken;
            return Task.FromResult(input);
        }
    }

    private class TokenCapturingPassthroughJunction : Junction<string, string>
    {
        public CancellationToken CapturedToken { get; private set; }

        public override Task<string> Run(string input)
        {
            CapturedToken = CancellationToken;
            return Task.FromResult(input);
        }
    }

    private class CountingJunction : Junction<string, string>
    {
        public int ExecutionCount { get; private set; }

        public override Task<string> Run(string input)
        {
            ExecutionCount++;
            return Task.FromResult(input);
        }
    }

    private class CancellationTriggerJunction : Junction<string, string>
    {
        private readonly CancellationTokenSource _cts;
        public int ExecutionCount { get; private set; }

        public CancellationTriggerJunction(CancellationTokenSource cts) => _cts = cts;

        public override Task<string> Run(string input)
        {
            ExecutionCount++;
            _cts.Cancel();
            return Task.FromResult(input);
        }
    }

    private class SlowJunction : Junction<string, string>
    {
        public override async Task<string> Run(string input)
        {
            // determinism: this delay exists to be cancelled by the supplied CancellationToken.
            // The test verifies that cancelling the token shortcuts the delay, so the duration
            // is an upper bound, not a fixed wait.
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken);
            return input;
        }
    }

    private class SingleJunctionTrain : Train<string, string>
    {
        private readonly Junction<string, string> _junction;

        public SingleJunctionTrain(Junction<string, string> junction) => _junction = junction;

        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(_junction).Resolve();
    }

    private class TwoJunctionTrain : Train<string, string>
    {
        private readonly Junction<string, string> _junction1;
        private readonly Junction<string, string> _junction2;

        public TwoJunctionTrain(
            Junction<string, string> junction1,
            Junction<string, string> junction2
        )
        {
            _junction1 = junction1;
            _junction2 = junction2;
        }

        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Activate(input).Chain(_junction1).Chain(_junction2).Resolve();
    }

    #endregion
}
