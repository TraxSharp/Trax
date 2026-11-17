using System.Globalization;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>An effect that blocks until released — lets a test hold a claim mid-flight deterministically
/// (no sleeps) to prove a second caller gets an in-progress result.</summary>
public sealed class GatedEffect : ISnapshotEffect
{
    private readonly TaskCompletionSource _gate = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _entered = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Completes once the effect has started running (and is therefore holding the claim).</summary>
    public Task Entered => _entered.Task;

    public void Release() => _gate.TrySetResult();

    public async Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _calls);
        _entered.TrySetResult();
        await _gate.Task.ConfigureAwait(false);
        return string.Create(CultureInfo.InvariantCulture, $"receipt-{n}");
    }
}
