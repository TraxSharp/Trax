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

/// <summary>
/// A live answer the run would not act on: missing, or not fitting its question.
/// </summary>
/// <param name="Train">The train that asked.</param>
/// <param name="RunId">The run's external id.</param>
/// <param name="Question">The question as it was asked.</param>
/// <param name="Occurrence">How many times this run asked the question before, from 0.</param>
/// <param name="Fingerprint">
/// Identifies this asking of the question, as <see cref="DecisionMade.Fingerprint"/> does.
/// </param>
/// <param name="Answer">What the decider answered, or null when it gave no answer.</param>
/// <param name="Decider">The decider that was asked.</param>
/// <param name="Reason">
/// Why the answer was not acted on, such as <c>the decider answered 'Lane' with 'Banana', which is
/// not one of its options</c>.
/// </param>
public sealed record DecisionRefused(
    string Train,
    string RunId,
    Question Question,
    int Occurrence,
    string Fingerprint,
    Answer? Answer,
    Type Decider,
    string Reason
)
{
    /// <summary>
    /// The type the question was asked about: the enum a choice or score is between, or the marker
    /// type a yes or no question is about. Its <see cref="Question.Key"/> is derived from it (see
    /// <see cref="QuestionKey.For(Type)"/>). Null only on a record built outside a run.
    /// </summary>
    /// <remarks>
    /// Given so a host can treat questions by type rather than by key, for instance to keep
    /// answers about a sensitive type out of a journal, matching it with inheritance
    /// (<see cref="Type.IsAssignableTo(Type)"/>) where a key would only match one name.
    /// </remarks>
    public Type? QuestionType { get; init; }
}

/// <summary>
/// One answered question.
/// </summary>
/// <param name="Train">The train that asked.</param>
/// <param name="RunId">The run's external id, which ties the decision to the run that made it.</param>
/// <param name="Question">The question as it was asked.</param>
/// <param name="Occurrence">
/// How many times this run asked the question before, from 0: the number
/// <see cref="IDecisionReplay.Replay"/> is given to find this answer again.
/// </param>
/// <param name="Fingerprint">
/// Identifies this asking of the question as the chain declares it: the step, the state's type,
/// the question's words and its options or levels, never the state's value. A host that replays
/// stores it with the answer and returns it in <see cref="RecordedAnswer.Fingerprint"/>, and an
/// answer whose fingerprint differs from the asking it is replayed into is not acted on.
/// </param>
/// <param name="Answer">The answer the run acts on.</param>
/// <param name="Decider">The decider that answered, or null when the answer was replayed.</param>
/// <param name="Replayed">True when the answer came from an earlier run rather than a decider.</param>
/// <param name="Shadows">
/// What each shadow decider answered to the same question, for comparison only. Empty when the
/// answer was replayed, because shadows are not asked a replayed question.
/// </param>
/// <param name="ReplayRefused">
/// Why an answer recorded for this question by an earlier run was not replayed, so the decider
/// was asked afresh, or null when there was none to refuse. It says which check refused it: an
/// answer given to a different asking of the question, one that no longer fits the question, or
/// one that cannot be shown to be about the same state (see <see cref="StateHash"/>): given about
/// a different state, recorded without a hash, or recorded with or without a state hash key when
/// this run hashes the other way or could not resolve its key. It says what does not fit and
/// never quotes the recorded answer.
/// </param>
public sealed record DecisionMade(
    string Train,
    string RunId,
    Question Question,
    int Occurrence,
    string Fingerprint,
    Answer Answer,
    Type? Decider,
    bool Replayed,
    IReadOnlyList<ShadowAnswer> Shadows,
    string? ReplayRefused = null
)
{
    /// <summary>
    /// The hash of the state the question was asked about, taken before the decider was asked:
    /// <c>k1:</c> and the lower-case hex of an HMAC-SHA256 under the <see cref="StateHashKey"/> the
    /// train's container supplies, or <c>s1:</c> and the lower-case hex of a SHA-256 when it
    /// supplies none. It covers every instance field of the state's runtime type, public or
    /// not (so auto-properties, tuple items, record members and a derived type's members held where
    /// a base type is declared all count), and every value they hold, recursively, because an
    /// in-process decider can read all of them. A framework collection counts as its elements and
    /// its comparers, and a wrapper of one (a read-only wrapper, a <c>Collection&lt;T&gt;</c>, a
    /// view of a dictionary's keys) as what it wraps. Null when the state cannot be read the same
    /// way every time: it holds a reference cycle, a delegate, a pointer or handle, a type or
    /// member, a stream, task or thread, a culture other than the framework's shared instance of
    /// it, a non-generic hashtable or sorted list, a blocking collection, a wrapper made by
    /// <c>ArrayList</c>, a collection that enumerates by its own code, a field whose read throws,
    /// or is nested deeper than 64 or larger than the hash allows (1,000,000 values or 16 MiB of
    /// encoding). Null too when the container's key cannot be resolved.
    /// </summary>
    /// <remarks>
    /// A host that replays stores it with the answer and returns it in
    /// <see cref="RecordedAnswer.StateHash"/>. An answer is replayed only into an asking whose
    /// state hashes exactly the same, so a recorded answer replays only into the same state. A
    /// keyed and an unkeyed hash never match.
    ///
    /// <para>It covers the state's value only. What a decider reads from elsewhere, such as a
    /// customer it looks up by an id the state holds, is not in it, so a change there counts only
    /// when the state carries the value itself.</para>
    ///
    /// <para>Only the hash leaves the run, never the state. The hash covers values a host may mask
    /// or withhold elsewhere, so a host that records decisions should register a
    /// <see cref="StateHashKey"/>, the same in every process that may repeat a run.</para>
    /// </remarks>
    public string? StateHash { get; init; }

    /// <summary>
    /// The type of the state the question was asked about, which <see cref="StateHash"/> was
    /// taken of: the state's runtime type, or the type the step declared it as when the state was
    /// null. Null only on a record built outside a run.
    /// </summary>
    /// <remarks>
    /// Given so a host can treat a decision by what it was about, for instance to mask or keep out
    /// of a journal what it records about a sensitive state type, matching with inheritance
    /// (<see cref="Type.IsAssignableTo(Type)"/>).
    /// </remarks>
    public Type? StateType { get; init; }

    /// <summary>
    /// The type the question was asked about: the enum a choice or score is between, or the marker
    /// type a yes or no question is about. Its <see cref="Question.Key"/> is derived from it (see
    /// <see cref="QuestionKey.For(Type)"/>). Null only on a record built outside a run.
    /// </summary>
    /// <remarks>
    /// Given so a host can treat questions by type rather than by key, for instance to keep
    /// answers about a sensitive type out of a journal, matching it with inheritance
    /// (<see cref="Type.IsAssignableTo(Type)"/>) where a key would only match one name.
    /// </remarks>
    public Type? QuestionType { get; init; }
}

/// <summary>
/// A shadow decider's answer, which the run never acts on.
/// </summary>
/// <param name="Decider">The shadow decider.</param>
/// <param name="Answer">Its answer, or null when it failed or did not answer in time.</param>
/// <param name="Agrees">
/// Whether its answer would have done what the live answer did. When the step that asked also
/// routes (<c>Switch</c>, <c>Gate</c> or <c>Scale</c> with a state), that is taking the same
/// track by the step's own bars and bands. For a plain <c>Decide</c>, whose routing comes later,
/// it is the same option, the same nearest level, or the same side of one half for a yes/no.
/// False when it did not answer, answered with something that does not fit the question, or when
/// the step had no track to take on the live answer.
/// </param>
/// <param name="Error">Why it gave no answer, or null when it did.</param>
public sealed record ShadowAnswer(Type Decider, Answer? Answer, bool Agrees, string? Error);

/// <summary>
/// One routing step's outcome.
/// </summary>
/// <param name="Train">The train that was routed.</param>
/// <param name="RunId">The run's external id.</param>
/// <param name="On">The enum or marker type the routing was on.</param>
/// <param name="Track">The track taken.</param>
/// <param name="FallbackReason">Why the decision was not followed, or null when it was.</param>
public sealed record TrackRouted(
    string Train,
    string RunId,
    Type On,
    string Track,
    string? FallbackReason
);

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

/// <summary>
/// An answer an earlier run acted on, as a host recorded it.
/// </summary>
/// <param name="Answer">The answer, as <see cref="DecisionMade.Answer"/> carried it.</param>
/// <param name="Fingerprint">
/// The <see cref="DecisionMade.Fingerprint"/> the answer was recorded with, exactly as given.
/// </param>
public sealed record RecordedAnswer(Answer Answer, string Fingerprint)
{
    /// <summary>
    /// The <see cref="DecisionMade.StateHash"/> the answer was recorded with, exactly as given.
    /// An answer is replayed only when it equals the hash of the state asked about now, so one
    /// left null, such as an answer recorded before states were hashed, is never replayed.
    /// </summary>
    public string? StateHash { get; init; }
}
