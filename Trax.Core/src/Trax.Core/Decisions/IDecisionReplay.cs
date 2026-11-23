namespace Trax.Core.Decisions;

/// <summary>
/// Supplies answers a run already gave, so a run that is repeated takes the same tracks instead of
/// asking again and possibly being answered differently.
/// </summary>
/// <remarks>
/// Optional: found in Memory, then in the container. A host that requeues runs implements it from
/// what it recorded through <see cref="IDecisionObserver"/>: the
/// <see cref="DecisionMade.Answer"/>, <see cref="DecisionMade.Fingerprint"/> and
/// <see cref="DecisionMade.StateHash"/>, keyed by train,
/// run, <see cref="Question.Key"/> and <see cref="DecisionMade.Occurrence"/>. It is asked once per
/// question, on the run's path, before the decider, so a lookup should be quick.
///
/// <para>A replayed answer is checked before it is acted on. One whose fingerprint differs from the
/// question as it is asked now was given to a different asking (the question was reworded, its
/// options or levels changed, or the chain changed so that another step now asks it first), and
/// one that no longer fits the question (an option renamed or removed, a scale with fewer levels,
/// a different kind of question) cannot repeat what the earlier run did. Nor is one whose
/// <see cref="RecordedAnswer.StateHash"/> differs from the state asked about now, is null, or
/// cannot be compared because the state cannot be hashed: a recorded answer replays only into
/// the same state. None of these is acted on: the decider is asked afresh, with the
/// reason in <see cref="DecisionMade.ReplayRefused"/>. The check is made here, so every replay
/// inherits it and an implementation only stores and returns the hash. A recorded
/// choice of a member the step has no track for is replayed like any other, and takes the
/// fallback track again, as it did the first time. Shadows are not asked a question whose answer
/// is replayed.</para>
///
/// <para>Whatever it throws fails the step, classified as the exception says, because a replay
/// that cannot be read cannot be told from a run with nothing to replay.</para>
///
/// <para>It is asked on the asking step's own flow, with <see cref="Monad.ChainGraph.CurrentNodeId"/>
/// and <see cref="Monad.ChainGraph.CurrentBranchPath"/> already naming that step, as an observer
/// is told. Inside a <c>Parallel</c> branch the occurrence counts on from the fork, so two
/// branches asking the same question give the same occurrence: a host that replays keys its
/// answers by the branch path as well.</para>
/// </remarks>
public interface IDecisionReplay
{
    /// <summary>
    /// The answer to replay for the <paramref name="occurrence"/>th asking (from 0, as
    /// <see cref="DecisionMade.Occurrence"/> numbers it) of the question <paramref name="key"/> in
    /// the run <paramref name="runId"/>, with the fingerprint it was recorded under, or null to ask
    /// the decider.
    /// </summary>
    Task<RecordedAnswer?> Replay(
        string train,
        string runId,
        string key,
        int occurrence,
        CancellationToken cancellationToken
    );
}
