using Microsoft.EntityFrameworkCore;
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
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.Checkpoints.Add(row);
        await context.SaveChanges(cancellationToken);
    }

    public async Task<IReadOnlyList<ResumedRun>> Lineage(
        long runId,
        CancellationToken cancellationToken
    )
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
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
                .SingleOrDefaultAsync(cancellationToken);

            if (run is null)
                break;

            var rows = await context
                .Checkpoints.AsNoTracking()
                .Where(c => c.MetadataId == id)
                .ToListAsync(cancellationToken);

            lineage.Add(new ResumedRun(run.Id, run.ResumeFrom, run.ResumeAt, rows));
            next = run.ResumeFrom;
        }

        return lineage;
    }

    public async Task DeleteFor(long runId, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        // Read and removed rather than deleted in bulk: the InMemory provider has no bulk delete,
        // and a completed run has a handful of rows at most.
        var rows = await context
            .Checkpoints.Where(c => c.MetadataId == runId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return;

        context.Checkpoints.RemoveRange(rows);
        await context.SaveChanges(cancellationToken);
    }

    public bool HasUncommittedWork(IServiceProvider scope) =>
        scope.GetService(typeof(IDataContext)) is DbContext context
        && (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges());
}
