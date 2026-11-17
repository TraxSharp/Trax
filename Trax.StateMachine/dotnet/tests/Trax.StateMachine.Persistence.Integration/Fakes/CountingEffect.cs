namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
