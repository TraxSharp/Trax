namespace Trax.Core.Decisions;

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
