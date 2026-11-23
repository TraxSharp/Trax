using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Step;

public class JunctionCancellationTests : TestSetup
{
    [Theory]
    public async Task RailwayStep_SetsCancellationToken_BeforeCallingRun()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction = new TokenVerifyingStep();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        result.IsRight.Should().BeTrue();
        junction.TokenWasSetBeforeRun.Should().BeTrue();
    }

    [Theory]
    public async Task RailwayStep_WithCancelledToken_ThrowsBeforeRun()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);
        var junction = new CountingJunction();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act
        var act = () => junction.RailwayJunction(input, train);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        junction.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task RailwayStep_WithLeftInput_DoesNotCheckCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);
        var junction = new CountingJunction();
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = new InvalidOperationException("previous failure");

        // Act
        var result = await junction.RailwayJunction(input, train).ConfigureAwait(false);

        // Assert — short-circuits without throwing OperationCanceledException
        result.IsLeft.Should().BeTrue();
        junction.ExecutionCount.Should().Be(0);
    }

    [Theory]
    public async Task RailwayStep_CancellationException_NotWrappedInExceptionData()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var junction = new CancellingStep(cts);
        var train = new TestTrain();
        train.CancellationToken = cts.Token;

        Either<Exception, string> input = "hello";

        // Act & Assert
        await FluentActions
            .Invoking(() => junction.RailwayJunction(input, train))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        junction.ExceptionData.Should().BeNull();
    }

    [Theory]
    public async Task RailwayStep_NonCancellationException_StillWrapsInExceptionData()
    {
        // Arrange
        var junction = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        result.IsLeft.Should().BeTrue();
        junction.ExceptionData.Should().NotBeNull();
        junction.ExceptionData.NotNull().Junction.Should().Be(nameof(ThrowingStep));
    }

    [Theory]
    public async Task RailwayJunction_ExceptionThrown_OriginalMessagePreserved()
    {
        // Arrange
        var junction = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert — the exception message must be the original, not JSON
        var exception = result.Swap().ValueUnsafe();
        exception.Message.Should().Be("test error");
    }

    [Theory]
    public async Task RailwayJunction_ExceptionThrown_ExceptionDataAttachedViaDataDictionary()
    {
        // Arrange
        var junction = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert — TrainExceptionData stored in Exception.Data dictionary
        var exception = result.Swap().ValueUnsafe();
        exception.Data["TrainExceptionData"].Should().NotBeNull();
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data.NotNull().Junction.Should().Be(nameof(ThrowingStep));
        data.Type.Should().Be("InvalidOperationException");
        data.Message.Should().Be("test error");
        data.TrainName.Should().Be(nameof(TestTrain));
    }

    [Theory]
    public async Task RailwayJunction_ExceptionThrown_OriginalStackTraceInExceptionData()
    {
        // Arrange
        var junction = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert — the captured StackTrace should contain the junction's Run method
        var exception = result.Swap().ValueUnsafe();
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data.NotNull().StackTrace.Should().NotBeNullOrEmpty();
        data.StackTrace.Should().Contain(nameof(ThrowingStep));
    }

    [Theory]
    public async Task RailwayJunction_ExceptionThrown_ExceptionDataAndPropertyAreConsistent()
    {
        // Arrange
        var junction = new ThrowingStep();
        var train = new TestTrain();

        Either<Exception, string> input = "hello";

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert — Junction.ExceptionData property and Exception.Data dictionary should match
        var exception = result.Swap().ValueUnsafe();
        var dictionaryData = exception.Data["TrainExceptionData"] as TrainExceptionData;
        junction.ExceptionData.Should().BeSameAs(dictionaryData);
    }

    #region Test Helpers

    private sealed class TokenVerifyingStep : Junction<string, string>
    {
        public bool TokenWasSetBeforeRun { get; private set; }

        public override Task<string> Run(string input)
        {
            TokenWasSetBeforeRun = CancellationToken != CancellationToken.None;
            return Task.FromResult(input);
        }
    }

    private sealed class CountingJunction : Junction<string, string>
    {
        public int ExecutionCount { get; private set; }

        public override Task<string> Run(string input)
        {
            ExecutionCount++;
            return Task.FromResult(input);
        }
    }

    private sealed class CancellingStep : Junction<string, string>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingStep(CancellationTokenSource cts) => _cts = cts;

        public override async Task<string> Run(string input)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
            return input;
        }
    }

    private sealed class ThrowingStep : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("test error");
    }

    private sealed class TestTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }

    #endregion
}
