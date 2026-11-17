namespace Trax.Core.Decisions;

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
