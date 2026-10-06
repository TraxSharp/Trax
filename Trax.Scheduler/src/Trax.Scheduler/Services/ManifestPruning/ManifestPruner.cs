using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.ManifestPruning;

/// <summary>
/// Deletes manifests the code no longer declares, with their work queue rows, dead letters and
/// finished runs. Shared by the startup orphan prune and a batch's prune.
/// </summary>
/// <remarks>
/// <para>
/// A manifest with a run that is still <c>Pending</c> or <c>InProgress</c> is left alone, run and
/// all, and considered again at a later prune once the run has finished. The metadata delete also
/// filters on the state, so a run that starts between that check and the delete makes the
/// manifest delete fail on its foreign key and the batch roll back, rather than being deleted.
/// </para>
/// <para>
/// Each batch runs in one transaction, so a batch that fails deletes nothing. A run that another
/// run started records it as its parent (<c>metadata.parent_id</c>); that reference is cleared
/// before the parent is deleted and the child run is kept, as the metadata cleanup does.
/// </para>
/// </remarks>
internal static class ManifestPruner
{
    /// <summary>
    /// Maximum number of manifests deleted per batch. Keeps each <c>IN (...)</c> clause small
    /// enough to avoid command timeouts on large prune operations.
    /// </summary>
    internal const int BatchSize = 500;

    /// <summary>
    /// Deletes the given manifests in batches of <see cref="BatchSize"/>. A batch that fails is
    /// logged and skipped, and the rest carry on.
    /// </summary>
    /// <returns>The number of manifests deleted, and the number kept for an unfinished run.</returns>
    internal static async Task<(int Pruned, int KeptForActiveRuns)> PruneAsync(
        IDataContext context,
        IReadOnlyList<long> manifestIds,
        ILogger logger,
        CancellationToken ct
    )
    {
        var pruned = 0;
        var keptForActiveRuns = 0;

        foreach (var chunk in manifestIds.Chunk(BatchSize))
        {
            var batch = chunk.ToList();

            var withActiveRuns = await context
                .Metadatas.Where(m =>
                    m.ManifestId.HasValue
                    && batch.Contains(m.ManifestId.Value)
                    && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
                )
                .Select(m => m.ManifestId!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (withActiveRuns.Count > 0)
            {
                keptForActiveRuns += withActiveRuns.Count;
                logger.LogInformation(
                    "Keeping {Count} manifest(s) to prune until their pending or running runs finish: {ManifestIds}",
                    withActiveRuns.Count,
                    withActiveRuns
                );
                batch = batch.Except(withActiveRuns).ToList();
            }

            if (batch.Count == 0)
                continue;

            try
            {
                pruned += await DeleteBatchAsync(context, batch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Failed to prune a batch of {Count} manifest(s); they are kept and the prune continues: {ManifestIds}",
                    batch.Count,
                    batch
                );
            }
        }

        return (pruned, keptForActiveRuns);
    }

    private static async Task<int> DeleteBatchAsync(
        IDataContext context,
        List<long> batch,
        CancellationToken ct
    )
    {
        using var transaction = await context.BeginTransaction(ct);

        try
        {
            // Clear the self-referencing FK (DependsOnManifestId) for any manifest pointing to
            // one in this batch, whether that manifest is also being deleted or kept.
            await context
                .Manifests.Where(m =>
                    m.DependsOnManifestId.HasValue && batch.Contains(m.DependsOnManifestId.Value)
                )
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.DependsOnManifestId, (long?)null),
                    ct
                );

            await context
                .WorkQueues.Where(w => w.ManifestId.HasValue && batch.Contains(w.ManifestId.Value))
                .ExecuteDeleteAsync(ct);

            await context
                .DeadLetters.Where(d => batch.Contains(d.ManifestId))
                .ExecuteDeleteAsync(ct);

            var runIds = context
                .Metadatas.Where(m => m.ManifestId.HasValue && batch.Contains(m.ManifestId.Value))
                .Select(m => m.Id);

            // A run another run started, and a dead letter's retry, reference these runs without
            // being owned by them: clear the references rather than deleting the rows.
            await context
                .Metadatas.Where(m => m.ParentId.HasValue && runIds.Contains(m.ParentId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ParentId, (long?)null), ct);

            await context
                .DeadLetters.Where(d =>
                    d.RetryMetadataId.HasValue && runIds.Contains(d.RetryMetadataId.Value)
                )
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryMetadataId, (long?)null), ct);

            await context
                .Metadatas.Where(m =>
                    m.ManifestId.HasValue
                    && batch.Contains(m.ManifestId.Value)
                    && m.TrainState != TrainState.Pending
                    && m.TrainState != TrainState.InProgress
                )
                .ExecuteDeleteAsync(ct);

            var deleted = await context
                .Manifests.Where(m => batch.Contains(m.Id))
                .ExecuteDeleteAsync(ct);

            await context.CommitTransaction();
            return deleted;
        }
        catch
        {
            await context.RollbackTransaction();
            throw;
        }
    }
}
