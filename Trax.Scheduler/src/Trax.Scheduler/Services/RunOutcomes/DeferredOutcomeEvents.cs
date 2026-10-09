using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Scheduler.Services.RunOutcomes;

/// <summary>
/// Holds the runs a junction failed until the transaction that failed them commits, then
/// publishes their lifecycle events through <see cref="OutOfTrainOutcomes"/>. Scoped: one per
/// ManifestManager cycle.
/// </summary>
/// <remarks>
/// The ManifestManager runs inside the leader-lock transaction, which the polling service commits
/// after the whole chain. A reaper's write is not visible, and may yet be rolled back, until then,
/// so its events wait for <see cref="FlushAsync"/>, which the polling service calls after the
/// commit. Runs held by a scope whose transaction never commits are dropped with the scope. A
/// write made with no transaction open has committed already and is published at once.
/// <para>
/// A reaper can fail many runs in one cycle, and each publish may wait up to
/// <see cref="OutOfTrainOutcomes.PublishTimeout"/> on its hooks, so the held runs are published
/// <see cref="Parallelism"/> at a time. On SQLite, which takes one writer at a time, they are
/// published in order, so the hooks' own writes do not queue on each other.
/// </para>
/// </remarks>
internal sealed class DeferredOutcomeEvents(
    IServiceProvider services,
    ILogger<DeferredOutcomeEvents> logger
)
{
    /// <summary>How many held runs are published at once, where the store takes several writers.</summary>
    internal const int Parallelism = 8;

    private readonly List<long> _failed = [];

    private bool _oneWriter;

    /// <summary>
    /// Publishes <c>Failed</c> for <paramref name="runIds"/> once <paramref name="context"/>'s
    /// write of it has committed: now, when no transaction is open on it, otherwise at
    /// <see cref="FlushAsync"/>.
    /// </summary>
    internal async Task FailedAsync(IDataContext context, IReadOnlyCollection<long> runIds)
    {
        if (runIds.Count == 0)
            return;

        if (context is DbContext { Database.CurrentTransaction: not null } db)
        {
            lock (_failed)
            {
                _failed.AddRange(runIds);
                _oneWriter |=
                    db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                    == true;
            }
            return;
        }

        await OutOfTrainOutcomes.PublishFailedAsync(services, runIds, logger);
    }

    /// <summary>
    /// Publishes the events held for a transaction that has now committed, once each.
    /// </summary>
    internal async Task FlushAsync()
    {
        List<long> failed;
        bool oneWriter;
        lock (_failed)
        {
            failed = [.. _failed];
            _failed.Clear();
            oneWriter = _oneWriter;
        }

        if (failed.Count > 0)
            await OutOfTrainOutcomes.PublishFailedAsync(
                services,
                failed,
                logger,
                parallelism: oneWriter ? 1 : Parallelism
            );
    }
}
