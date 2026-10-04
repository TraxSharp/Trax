using Trax.Effect.Models.JunctionRun;

namespace Trax.Effect.Data.JunctionEvents;

/// <summary>
/// Reads a run's timeline back from <c>trax.junction_run</c>, as <c>AddJunctionEvents</c> wrote it.
/// </summary>
public static class JunctionRunQueries
{
    /// <summary>
    /// The steps of the run <paramref name="metadataId"/> records, in the order it reached them.
    /// </summary>
    /// <remarks>
    /// Use it on <c>IDataContext.JunctionRuns</c>, as in
    /// <c>await context.JunctionRuns.AsNoTracking().ForRun(id).ToListAsync(ct)</c>. Each position
    /// appears once: a junction's row is updated in place when it ends.
    ///
    /// <para>The rows are written off the run's path, so a running run's newest steps can reach a
    /// live subscriber a moment before they reach this table. A subscriber that joins late
    /// subscribes first, then reads the steps so far, and keeps for each position whichever of the
    /// two is further along (a later state, or an <c>EndedAt</c>). A step the writer dropped because
    /// it fell behind or the database was down is missing from the table, and a junction whose end
    /// was dropped keeps the <c>InProgress</c> row its start wrote: read a row still in progress
    /// together with its run's state, since a run that has ended has no junction still
    /// running.</para>
    /// </remarks>
    /// <param name="runs">The junction run rows, normally <c>IDataContext.JunctionRuns</c>.</param>
    /// <param name="metadataId">The run's <c>Metadata.Id</c>.</param>
    public static IOrderedQueryable<JunctionRun> ForRun(
        this IQueryable<JunctionRun> runs,
        long metadataId
    ) => runs.Where(r => r.MetadataId == metadataId).OrderBy(r => r.Position);
}
