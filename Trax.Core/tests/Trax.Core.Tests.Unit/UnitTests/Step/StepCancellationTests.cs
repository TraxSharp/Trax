using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Step;

public class StepCancellationTests : TestSetup
{
    [Theory]
    public async Task RailwayStep_SetsCancellationToken_BeforeCallingRun()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step = new TokenVerifyingStep();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act
        var result = await step.RailwayStep(input, train);

        // Assert
        result.IsRight.Should().BeTrue();
        step.TokenWasSetBeforeRun.Should().BeTrue();
    }

    [Theory]
    public async Task RailwayStep_WithCancelledToken_ThrowsBeforeRun()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new CountingStep();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act
        var act = () => step.RailwayStep(input, train);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        step.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task RailwayStep_WithLeftInput_DoesNotCheckCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var step = new CountingStep();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = new InvalidOperationException("previous failure");

        // Act
        var result = await step.RailwayStep(input, train);

        // Assert — short-circuits without throwing OperationCanceledException
        result.IsLeft.Should().BeTrue();
        step.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task RailwayStep_CancellationException_NotWrappedInExceptionData()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var step = new CancellingStep(cts);
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act & Assert
        await FluentActions
            .Invoking(() => step.RailwayStep(input, train))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        step.ExceptionData.Should().BeNull();
    }

    [Theory]
    public async Task RailwayStep_NonCancellationException_StillWrapsInExceptionData()
    {
        // Arrange
        var step = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await step.RailwayStep(input, train);

        // Assert
        result.IsLeft.Should().BeTrue();
        step.ExceptionData.Should().NotBeNull();
        step.ExceptionData!.Step.Should().Be(nameof(ThrowingStep));
    }

    #region Test Helpers

    private class TokenVerifyingStep : Step<string, string>
    {
        public bool TokenWasSetBeforeRun { get; private set; }

        public override Task<string> Run(string input)
        {
            TokenWasSetBeforeRun = CancellationToken != CancellationToken.None;
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

    private class CancellingStep : Step<string, string>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingStep(CancellationTokenSource cts) => _cts = cts;

        public override Task<string> Run(string input)
        {
            _cts.Cancel();
            CancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(input);
        }
    }

    private class ThrowingStep : Step<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("test error");
    }

    private class TestTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            throw new NotImplementedException();
    }

    #endregion
}
