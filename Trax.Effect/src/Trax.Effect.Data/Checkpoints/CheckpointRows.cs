using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.Checkpoints;

namespace Trax.Effect.Data.Checkpoints;

/// <summary>
/// The <c>trax.checkpoint</c> table over a data context of its own for each call, so writing a
/// checkpoint saves nothing else the run tracks (effect/0021) and works the same on every
/// provider, InMemory included.
/// </summary>
/// <remarks>See Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</remarks>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal sealed class CheckpointRows(IDataContextProviderFactory contexts) : ICheckpointRows
{
    /// <summary>
    /// How far back a lineage is followed. A run resumed more times than this has a lineage no one
    /// is following by hand, and the rows beyond it are older than any it would choose.
    /// </summary>
    internal const int MaxLineage = 64;

    public async Task Insert(
        Trax.Effect.Models.Checkpoint.Checkpoint row,
        CancellationToken cancellationToken
    )
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        context.Checkpoints.Add(row);
        await context.SaveChanges(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResumedRun>> Lineage(
        long runId,
        CancellationToken cancellationToken
    )
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        var lineage = new List<ResumedRun>();
        var seen = new HashSet<long>();
        long? next = runId;

        while (next is { } id && lineage.Count < MaxLineage && seen.Add(id))
        {
            var run = await context
                .Metadatas.AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => new
                {
                    m.Id,
                    m.ResumeFrom,
                    m.ResumeAt,
                })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (run is null)
                break;

            // Everything but the state, which can be a megabyte a row.
            var rows = await context
                .Checkpoints.AsNoTracking()
                .Where(c => c.MetadataId == id)
                .Select(c => new Effect.Models.Checkpoint.Checkpoint
                {
                    Id = c.Id,
                    MetadataId = c.MetadataId,
                    NodeId = c.NodeId,
                    BranchPath = c.BranchPath,
                    StateType = c.StateType,
                    // Not read here; the states are read by id once a resume picks its node.
                    State = string.Empty,
                    Tracks = c.Tracks,
                    ChainHash = c.ChainHash,
                    StateFingerprint = c.StateFingerprint,
                    CreatedAt = c.CreatedAt,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            lineage.Add(new ResumedRun(run.Id, run.ResumeFrom, run.ResumeAt, rows));
            next = run.ResumeFrom;
        }

        return lineage;
    }

    public async Task<IReadOnlyDictionary<long, string>> States(
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken
    )
    {
        if (rowIds.Count == 0)
            return new Dictionary<long, string>();

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        var ids = rowIds.ToList();

        return await context
            .Checkpoints.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.State, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ResumeSource?> Source(long runId, CancellationToken cancellationToken)
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);

        return await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == runId)
            .Select(m => new ResumeSource(
                m.Name,
                m.Input,
                m.TrainState,
                context.Metadatas.Any(r =>
                    r.ResumeFrom == runId && r.TrainState == Enums.TrainState.Completed
                )
            ))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteFor(long runId, CancellationToken cancellationToken)
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);

        // Read and removed rather than deleted in bulk: the InMemory provider has no bulk delete,
        // and a completed run has a handful of rows at most.
        var rows = await context
            .Checkpoints.Where(c => c.MetadataId == runId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return;

        context.Checkpoints.RemoveRange(rows);
        await context.SaveChanges(cancellationToken).ConfigureAwait(false);
    }

    public bool HasUncommittedWork(IServiceProvider scope) =>
        scope.GetService(typeof(IDataContext)) is DbContext context
        && (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges());
}
