namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public sealed class FakePrincipal(string? userKey) : ISnapshotPrincipal
{
    public string? CurrentUserKey => userKey;
}

/// <summary>The order machine's irreversible effect port (bound inline via RunsOnce&lt;IOrderCharge&gt;).</summary>
public interface IOrderCharge : IEffect { }

/// <summary>An effect that counts deliveries and returns a distinct receipt each time — so a test can prove
/// exactly-once from the call count and the receipt in the snapshot.</summary>
public sealed class CountingEffect(bool fail = false) : IOrderCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _calls);
        if (fail)
            throw new InvalidOperationException("delivery failed");
        return Task.FromResult($"receipt-{n}");
    }
}

/// <summary>An effect that blocks until released — lets a test hold a claim mid-flight deterministically
/// (no sleeps) to prove a second caller gets an in-progress result.</summary>
public sealed class GatedEffect : IEffect
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
        await _gate.Task;
        return $"receipt-{n}";
    }
}
