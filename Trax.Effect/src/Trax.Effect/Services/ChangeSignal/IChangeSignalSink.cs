namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Receives coalesced change signals from <see cref="ChangeSignalCoalescer"/>. Each flush
/// carries the distinct set of domains that changed within one coalesce window. Implementations
/// fan the signal out to a delivery mechanism: the in-process GraphQL topic
/// (<c>onDataChanged</c>) or the cross-process broadcaster.
/// </summary>
public interface IChangeSignalSink
{
    /// <summary>Delivers the distinct set of changed <paramref name="domains"/>.</summary>
    Task FlushAsync(IReadOnlyCollection<ChangeDomain> domains, CancellationToken ct);
}
