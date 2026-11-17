namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// How a train is queued, beyond its name and input.
/// </summary>
public sealed record QueueTrainOptions
{
    /// <summary>Dispatch priority (0–31, higher runs first).</summary>
    public int Priority { get; init; }

    /// <summary>
    /// Earliest time the entry may be dispatched, stored as UTC. Null dispatches as soon as a
    /// worker is free.
    /// </summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>
    /// The metadata id of an earlier run whose recorded decisions the new run replays, so it takes
    /// the tracks that run took instead of asking its deciders again. Set when repeating a run, by
    /// the operations service only, which checks the run it names first; an implementation reads it.
    /// </summary>
    public long? ReplayDecisionsOf { get; internal init; }

    /// <summary>
    /// The metadata id of a failed run the new run resumes, skipping to the step after its latest
    /// checkpoint (or to <see cref="ResumeAt"/>) instead of running every step again. See
    /// Trax.Docs/adr/0047. Set by the operations service only, which checks the run it names
    /// first; the resumed run checks it again as it starts.
    /// </summary>
    public long? ResumeFrom { get; internal init; }

    /// <summary>The step the new run resumes at, or null for after the latest checkpoint.</summary>
    public string? ResumeAt { get; internal init; }

    /// <summary>
    /// Records the current caller (<c>ICurrentPrincipalProvider</c>) as the run's starter, so that caller, and only
    /// that caller, may follow the run's progress (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md). Set by the GraphQL mutations; a queue from
    /// host code, the dashboard or the operations namespace leaves it off, and its run has no starter.
    /// </summary>
    public bool RecordCaller { get; init; }

    /// <summary>The starter to record whatever the caller is: a requeue keeps the original run's. Trax only.</summary>
    internal string? StarterHash { get; init; }
}
