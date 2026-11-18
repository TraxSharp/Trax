using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Counts deliveries and returns a distinct receipt each time, so the E2E can prove exactly-once from the
/// call count and the receipt in the returned snapshot. Registered as a singleton so the count survives
/// across the two send requests.
/// </summary>
public sealed class CountingCharge : IOrderCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _calls);
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"receipt-{n}"));
    }
}
