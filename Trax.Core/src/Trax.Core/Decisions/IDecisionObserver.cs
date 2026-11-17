namespace Trax.Core.Decisions;

/// <summary>
/// Told about every decision a train makes and every track it takes, so a host can record them
/// against the run.
/// </summary>
/// <remarks>
/// Optional: found in Memory, then in the container, and skipped when neither holds one. It is
/// awaited on the train's path, so it should record and return.
///
/// <para>By default an observer is best effort: whatever it throws is logged and ignored, because
/// recording a decision must never change it. An observer whose record the host depends on, such
/// as one that a later replay reads, sets <see cref="Required"/>, and then a failure to record
/// fails the step before the train acts on the decision, classified as the exception says or as
/// <c>Transient</c> when it says nothing. An observer that cannot even be resolved fails the step
/// either way, because whether it was required cannot be read from an observer that was never
/// built.</para>
/// </remarks>
public interface IDecisionObserver
{
    /// <summary>
    /// True when a failure to record must fail the step rather than be ignored. Defaults to false.
    /// </summary>
    bool Required => false;

    /// <summary>
    /// A question was answered (or replayed), after the answer was checked and before any track
    /// is taken on it.
    /// </summary>
    Task Decided(DecisionMade decision, CancellationToken cancellationToken);

    /// <summary>A routing step is sending the train down a track, before the track runs.</summary>
    Task Routed(TrackRouted routing, CancellationToken cancellationToken);

    /// <summary>
    /// The live decider gave no answer to a question, or one that does not fit it, so the step is
    /// about to fail rather than act on it. Told once for each such question, before the step
    /// fails; the run's failure carries the same reasons.
    /// </summary>
    /// <remarks>
    /// Defaults to doing nothing. A refusal is never replayed: the step failed on it, and asking
    /// again is what the failure's <c>Transient</c> class is for. A cascade that escalated an
    /// answer and ended with one that fits is not a refusal, and neither is a shadow's answer,
    /// which <see cref="ShadowAnswer.Error"/> reports. A failure to record a refusal is logged and
    /// never replaces the refusal as the reason the step failed, even for an observer that is
    /// <see cref="Required"/>.
    /// </remarks>
    Task Refused(DecisionRefused refusal, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
