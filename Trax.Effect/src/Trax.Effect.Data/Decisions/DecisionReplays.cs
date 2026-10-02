using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Decisions;

/// <summary>
/// What a host that requeues runs asks before it queues one to replay the decisions of another.
/// </summary>
public static class DecisionReplays
{
    /// <summary>
    /// Whether a requeue of the run <paramref name="metadataId"/> should replay its decisions,
    /// rather than be queued as an ordinary run that asks afresh.
    /// </summary>
    /// <remarks>
    /// True when the run recorded a decision it acted on (an answer it refused does not count, since
    /// it is never replayed), or was itself queued to replay another run's. The
    /// second matters for a requeue of a requeue: one that failed before it reached a question
    /// recorded nothing, but the answers of the run it replayed are still the ones to repeat, and
    /// the replay follows <c>replay_decisions_of</c> back to them. False for a run of a train that
    /// never decides, a run on a host that records nothing, and a run that does not exist.
    /// </remarks>
    public static async Task<bool> HasDecisionsToReplay(
        this IDataContext context,
        long metadataId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        var replays = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => m.ReplayDecisionsOf)
            .FirstOrDefaultAsync(cancellationToken);

        return replays is not null
            || await context
                .RecordedDecisions.AsNoTracking()
                .AnyAsync(d => d.MetadataId == metadataId && d.Refused == null, cancellationToken);
    }
}
