namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// What a batch trigger did, returned by <see cref="IOperationsService.TriggerManifestsAsync"/>
/// and <see cref="IOperationsService.TriggerManifestGroupsAsync"/>. The counts are of manifests,
/// whichever of the two was called.
/// </summary>
/// <param name="Success">
/// False only when the batch was refused as given (no ids, or more than
/// <see cref="OperationsService.MaxBatchSize"/>) or nothing on the host can dispatch a queued run
/// (<see cref="OperationsService.NoDispatcherMessage"/>), in which case nothing was triggered and
/// every count is zero.
/// </param>
/// <param name="Matched">
/// How many of the given ids named a manifest (or a group) that exists and was triggered.
/// </param>
/// <param name="Queued">Manifests a new work queue entry was queued for.</param>
/// <param name="AlreadyQueued">
/// Manifests that already had a queued entry, so nothing more was queued: that entry became the
/// triggered run, marked as asked for by name and brought forward to now when it was due later.
/// Also counts an entry the dispatcher claimed before the trigger reached it, which is already
/// running.
/// </param>
/// <param name="TooLateToAskAfresh">
/// Manifests triggered to ask afresh whose queued retry the dispatcher claimed first, so the run
/// replays the decisions of the run it retries anyway. Always zero unless the trigger asked
/// afresh. Each has a line in <paramref name="Notes"/> naming the run it replays.
/// </param>
/// <param name="Skipped">Given ids that named no manifest (or no group). Each has a line in <paramref name="Notes"/>.</param>
/// <param name="Message">One line saying what happened, for an operator.</param>
/// <param name="Notes">
/// One line per id that did not get what the trigger asked for: skipped as unknown, or still
/// replaying. Empty when every id was triggered as asked.
/// </param>
public record BatchTriggerResult(
    bool Success,
    int Matched,
    int Queued,
    int AlreadyQueued,
    int TooLateToAskAfresh,
    int Skipped,
    string Message,
    IReadOnlyList<BatchItemNote> Notes
)
{
    /// <summary>The result for a batch refused as given: nothing triggered.</summary>
    internal static BatchTriggerResult Refused(string message) =>
        new(false, 0, 0, 0, 0, 0, message, []);
}

/// <summary>One id in a batch, and what happened to it that the caller should know.</summary>
/// <param name="Id">The id as the caller gave it: a manifest id or a group id.</param>
/// <param name="Message">What happened to it.</param>
public record BatchItemNote(long Id, string Message);
