namespace Trax.Api.DTOs;

/// <summary>
/// What a batch trigger did: <c>triggerManifests</c> or <c>triggerGroups</c>. The counts are of
/// manifests either way. A batch that could not be taken as given (no ids, or more than 1000)
/// returns <c>Success = false</c> with every count zero, having triggered nothing.
/// </summary>
/// <param name="Success">False only when the batch was refused as given; then nothing was triggered.</param>
/// <param name="Matched">How many of the given ids named a manifest (or a group) that exists and was triggered.</param>
/// <param name="Queued">Manifests a new work queue entry was queued for.</param>
/// <param name="AlreadyQueued">
/// Manifests that already had a queued entry, so nothing more was queued: that entry became the
/// triggered run and was brought forward to now when it was due later. Also counts an entry the
/// dispatcher claimed before the trigger reached it, which is already running.
/// </param>
/// <param name="TooLateToAskAfresh">
/// Manifests triggered to ask afresh whose queued retry the dispatcher claimed first, so that run
/// replays the decisions of the run it retries anyway. Zero unless the trigger asked afresh. Each
/// has a line in <paramref name="Notes"/> naming the run it replays.
/// </param>
/// <param name="Skipped">Given ids that named no manifest (or no group). Each has a line in <paramref name="Notes"/>.</param>
/// <param name="Message">One line saying what happened, for an operator.</param>
/// <param name="Notes">
/// One line per id that did not get what the trigger asked for: unknown, or still replaying.
/// Empty when every id was triggered as asked.
/// </param>
public record BatchTriggerResponse(
    bool Success,
    int Matched,
    int Queued,
    int AlreadyQueued,
    int TooLateToAskAfresh,
    int Skipped,
    string Message,
    IReadOnlyList<BatchTriggerNote> Notes
);

/// <summary>One id in a batch trigger, and what happened to it that the caller should know.</summary>
/// <param name="Id">The id as given: a manifest id, or a group id.</param>
/// <param name="Message">What happened to it.</param>
public record BatchTriggerNote(long Id, string Message);
