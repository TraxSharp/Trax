using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A test's hold on a <see cref="PausingSnapshotStore"/>: armed, the store's next write waits after the caller's
/// read until the test releases it, so a test can put another write between a client's read and its update.
/// </summary>
public sealed class PauseGate
{
    private TaskCompletionSource _reached = New();
    private TaskCompletionSource _release = New();
    private int _armed;

    /// <summary>Completes once a write has been held.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Holds the next write through the store.</summary>
    public void Arm()
    {
        _reached = New();
        _release = New();
        Volatile.Write(ref _armed, 1);
    }

    /// <summary>Lets the held write go on.</summary>
    public void Release() => _release.TrySetResult();

    /// <summary>Disarms the gate and lets any held write go on.</summary>
    public void Reset()
    {
        Volatile.Write(ref _armed, 0);
        _release.TrySetResult();
    }

    internal async Task HoldIfArmed()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
            return;
        _reached.TrySetResult();
        await _release.Task.ConfigureAwait(false);
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
