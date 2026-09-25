using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Finds the queued sibling that dispatch would offer ahead of a given entry for the same subject.
/// </summary>
public static class SubjectSiblingQueries
{
    /// <summary>
    /// The confirmed, queued entries for <paramref name="entry"/>'s subject that dispatch would
    /// take before it, most-favoured first.
    /// </summary>
    /// <remarks>
    /// This has to agree with <c>LoadQueuedJobsJunction</c>, because the page uses it to tell an
    /// operator what their entry is waiting on. It previously compared priority and age alone and
    /// so named siblings dispatch would never have offered: an entry scheduled for tomorrow is not
    /// a candidate today, and nor is one whose manifest group is disabled, yet an entry due now was
    /// told it was waiting behind them while dispatch was about to take it.
    /// <para>
    /// Group priority leads dispatch order and is deliberately not compared. Only the mediator's
    /// queue path sets a subject key and those entries carry no manifest, so every sibling of one
    /// subject scores the same zero; priority then age is what separates them. The group filter is
    /// still applied, so the two do not drift apart again if that ever changes.
    /// </para>
    /// </remarks>
    public static IQueryable<WorkQueue> DispatchedAheadOf(
        this IQueryable<WorkQueue> source,
        WorkQueue entry,
        DateTime now
    ) =>
        source
            .Where(b =>
                b.SubjectKey == entry.SubjectKey
                && b.Id != entry.Id
                && b.Status == WorkQueueStatus.Queued
                && b.ConfirmedAt != null
                && (b.ScheduledAt == null || b.ScheduledAt <= now)
                && (b.ManifestId == null || b.Manifest!.ManifestGroup!.IsEnabled)
                && (
                    b.Priority > entry.Priority
                    || (b.Priority == entry.Priority && b.CreatedAt < entry.CreatedAt)
                )
            )
            .OrderByDescending(b => b.Priority)
            .ThenBy(b => b.CreatedAt);
}
