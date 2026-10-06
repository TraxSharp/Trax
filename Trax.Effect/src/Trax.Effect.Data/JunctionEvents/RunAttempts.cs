using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Data.JunctionEvents;

/// <summary>
/// Works out which attempt of its manifest a run is, from the manifest's earlier runs, through a
/// short-lived data context of its own. Registered by <c>AddJunctionEvents</c>.
/// </summary>
/// <remarks>
/// <para>The runs are picked as Trax.Scheduler picks them when it counts a manifest's failures: a
/// run that completed or was cancelled ends the streak, and a dispatch attempt the scheduler failed
/// and requeued (<c>FailureException</c> <see cref="RequeuedDispatch"/>) is not a run of the job
/// and is skipped, as is a run still pending or in progress. A dead letter's resolution does not
/// restart the count here; the scheduler's failure window and dead letters decide retries, this
/// only numbers them for a timeline.</para>
///
/// <para>It is one query over the manifest's <see cref="MaxRunsRead"/> most recent runs before
/// this one, read newest first through <c>ix_metadata_manifest_id_id</c>, reading each one's state
/// and failure exception, so its cost does not grow with the manifest's history. A streak longer
/// than that is reported as <see cref="MaxRunsRead"/> + 1.</para>
/// </remarks>
internal sealed class RunAttempts(IDataContextProviderFactory contexts) : IRunAttempts
{
    /// <summary>
    /// The failure exception Trax.Scheduler records on a dispatch attempt it requeued
    /// (<c>DispatchFailure.Requeued</c>, internal there). Keep the two equal.
    /// </summary>
    internal const string RequeuedDispatch = "DispatchRequeued";

    /// <summary>How many of the manifest's most recent runs are read.</summary>
    internal const int MaxRunsRead = 1000;

    /// <inheritdoc />
    public async Task<int?> AttemptOf(Metadata metadata, CancellationToken cancellationToken)
    {
        if (metadata.ManifestId is not { } manifestId)
            return null;

        using var context = await contexts.CreateDbContextAsync(cancellationToken);

        var id = metadata.Id;
        var recent = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifestId && m.Id < id)
            .OrderByDescending(m => m.Id)
            .Take(MaxRunsRead)
            .Select(m => new { m.TrainState, m.FailureException })
            .ToListAsync(cancellationToken);

        var failed = 0;

        // The streak ends at the latest run that completed or was cancelled.
        foreach (var run in recent)
        {
            if (run.TrainState is TrainState.Completed or TrainState.Cancelled)
                break;

            if (run.TrainState == TrainState.Failed && run.FailureException != RequeuedDispatch)
                failed++;
        }

        return failed + 1;
    }
}
