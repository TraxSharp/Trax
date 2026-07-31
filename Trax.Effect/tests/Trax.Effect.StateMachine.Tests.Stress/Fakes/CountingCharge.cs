using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Effect.StateMachine.Tests.Stress.Fakes;

/// <summary>
/// Counts total deliveries atomically and returns a distinct receipt each time. Shared across a whole fan-out
/// so a test proves exactly-once at scale from the total count: N instances charged once each means exactly
/// N calls, no matter how many concurrent sends raced for each one.
/// </summary>
public sealed class CountingCharge : IEffect
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _calls);
        return Task.FromResult($"receipt-{n}");
    }
}
