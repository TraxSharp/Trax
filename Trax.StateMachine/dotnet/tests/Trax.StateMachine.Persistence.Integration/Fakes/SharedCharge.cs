using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>An effect that counts its runs, shared by every scope so a test reads one count.</summary>
public sealed class SharedCharge : IOrderCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult($"receipt-{Interlocked.Increment(ref _calls)}");
}
