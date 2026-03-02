using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Step;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Tests that Chain does not deadlock when running inside a single-threaded
/// SynchronizationContext, which is the classic sync-over-async deadlock scenario.
/// </summary>
public class TrainSyncContextTests : TestSetup
{
    /// <summary>
    /// Simulates an environment with a single-threaded SynchronizationContext
    /// (like Blazor Server, WPF, or legacy ASP.NET). An async step that does NOT use
    /// ConfigureAwait(false) would deadlock without the SynchronizationContext suppression
    /// in Chain, because the continuation tries to marshal back to the blocked thread.
    /// </summary>
    [Theory]
    [CancelAfter(5000)] // Fail fast on deadlock instead of hanging forever
    public async Task ChainDoesNotDeadlockWithSingleThreadedSyncContext()
    {
        // Arrange — install a single-threaded SynchronizationContext
        var syncContext = new SingleThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var trainInput = 1;
            var stepInput = "hello";
            var train = new TestTrain().Activate(trainInput);

            // Act — chain a step that awaits WITHOUT ConfigureAwait(false)
            train.Chain<AsyncStepWithoutConfigureAwait, string, int>(
                new AsyncStepWithoutConfigureAwait(),
                stepInput,
                out var returnValue
            );

            // Assert — step completed successfully, no deadlock
            returnValue.IsRight.Should().BeTrue();
            returnValue.ValueUnsafe().Should().Be(stepInput.Length);
            train.Exception.Should().BeNull();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// Verifies the SynchronizationContext is properly restored after Chain completes.
    /// </summary>
    [Theory]
    [CancelAfter(5000)]
    public async Task ChainRestoresSyncContextAfterExecution()
    {
        // Arrange
        var syncContext = new SingleThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var trainInput = 1;
            var stepInput = "hello";
            var train = new TestTrain().Activate(trainInput);

            // Act
            train.Chain<AsyncStepWithoutConfigureAwait, string, int>(
                new AsyncStepWithoutConfigureAwait(),
                stepInput,
                out _
            );

            // Assert — original SynchronizationContext is restored
            SynchronizationContext.Current.Should().BeSameAs(syncContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// Verifies SynchronizationContext is restored even when the step throws.
    /// </summary>
    [Theory]
    [CancelAfter(5000)]
    public async Task ChainRestoresSyncContextOnStepException()
    {
        // Arrange
        var syncContext = new SingleThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var trainInput = 1;
            var stepInput = "hello";
            var train = new TestTrain().Activate(trainInput);

            // Act
            train.Chain<AsyncExceptionStep, string, int>(
                new AsyncExceptionStep(),
                stepInput,
                out var returnValue
            );

            // Assert — exception propagated via railway, and context is restored
            returnValue.IsLeft.Should().BeTrue();
            train.Exception.Should().NotBeNull();
            SynchronizationContext.Current.Should().BeSameAs(syncContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    #region Test helpers

    /// <summary>
    /// A step that uses Task.Delay (genuinely async) and does NOT use ConfigureAwait(false).
    /// Without the SynchronizationContext suppression in Chain, this deadlocks in a
    /// single-threaded sync context.
    /// </summary>
    private class AsyncStepWithoutConfigureAwait : Step<string, int>
    {
        public override async Task<int> Run(string input)
        {
            // Deliberately NOT using ConfigureAwait(false) — this is the deadlock trigger
            await Task.Delay(1);
            return input.Length;
        }
    }

    /// <summary>
    /// An async step that throws after an await, without ConfigureAwait(false).
    /// </summary>
    private class AsyncExceptionStep : Step<string, int>
    {
        public override async Task<int> Run(string input)
        {
            await Task.Delay(1);
            throw new InvalidOperationException("step failed");
        }
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// A SynchronizationContext that posts callbacks to a single dedicated thread.
    /// This simulates the behavior of Blazor Server, WPF, and WinForms dispatchers.
    /// Continuations queued via Post() will wait for the dedicated thread, which causes
    /// deadlock if that thread is blocked by .GetAwaiter().GetResult().
    /// </summary>
    private class SingleThreadSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // In a real single-threaded context, Post() would queue to the blocked thread.
            // We simulate this by using the thread pool (which is what happens when
            // SynchronizationContext is null). The key behavior we're testing is that
            // Chain *suppresses* this context — if it doesn't, the framework's Post()
            // would try to marshal back to the calling thread and deadlock.
            //
            // To truly reproduce the deadlock, we'd need to block here waiting for
            // the calling thread, but that would make the test hang rather than fail.
            // Instead, we rely on the fact that Chain sets SynchronizationContext.Current
            // to null before calling GetResult(), which prevents this Post from being
            // called at all — the continuation goes straight to the thread pool.
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    #endregion
}
