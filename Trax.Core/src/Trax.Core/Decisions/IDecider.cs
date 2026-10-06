namespace Trax.Core.Decisions;

/// <summary>
/// Answers typed questions about one state: which of a set of options, where on an ordered scale,
/// or how likely yes.
/// </summary>
/// <remarks>
/// This is the seam a train's decisions go through. A decision model that returns typed,
/// calibrated answers implements it in an adapter, and so can a rule table
/// (<see cref="RuleDecider"/>), a classifier, a larger model for escalation
/// (<see cref="CascadingDecider"/>), or a test double. Trax turns the train's typed declaration into
/// questions and turns the answers back into typed decisions, so an implementation never sees a
/// .NET type it must understand and never hands back one the train did not offer.
///
/// <para>A decider is found the way a junction input is: in Memory, then in the container. One
/// registration serves every decision in every train; <c>DecidedBy&lt;TDecider&gt;</c> names a
/// different one for a single decision.</para>
/// </remarks>
public interface IDecider
{
    /// <summary>
    /// Answers every question in <paramref name="request"/>.
    /// </summary>
    /// <returns>
    /// One answer per question, keyed by <see cref="Question.Key"/>. Throwing fails the run: an
    /// error is not a decision, so no track is taken on its behalf.
    /// </returns>
    Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The questions a train asks about one state.
/// </summary>
/// <param name="Train">The name of the train asking, for logging.</param>
/// <param name="State">
/// The value the questions are about. The live decider is handed the instance in the run's Memory,
/// not a copy, and reads it without changing it. A shadow is handed a copy of its own, made through
/// JSON, so nothing it does to it reaches the run or another shadow.
/// </param>
/// <param name="Questions">The questions, each with a key unique within the request.</param>
public sealed record DecisionRequest(string Train, object State, IReadOnlyList<Question> Questions);

/// <summary>
/// One question. The key identifies it in the answers; everything a model needs to know is in
/// <see cref="Instructions"/> and the criteria.
/// </summary>
/// <remarks>
/// The key is <see cref="QuestionKey.For(Type)"/> of the type the question is about: the
/// <see cref="AsksAttribute.Key"/> on it, or else its name without the namespace. It is not meant
/// to inform a model, but it is not hidden from one either: an adapter may send it as the
/// question's id, as the System One adapter does.
/// </remarks>
/// <param name="Key">Identifies the question in <see cref="DecisionResult.Answers"/>.</param>
/// <param name="Instructions">What is being asked, in words.</param>
public abstract record Question(string Key, string Instructions);

/// <summary>
/// Pick one of <paramref name="Options"/>. Answered with a <see cref="ChoiceAnswer"/>.
/// </summary>
public sealed record ChoiceQuestion(
    string Key,
    string Instructions,
    IReadOnlyList<Criterion> Options
) : Question(Key, Instructions);

/// <summary>
/// Place the state on <paramref name="Levels"/>, which run from lowest to highest. Answered with a
/// <see cref="ScoreAnswer"/>.
/// </summary>
public sealed record ScoreQuestion(string Key, string Instructions, IReadOnlyList<Criterion> Levels)
    : Question(Key, Instructions);

/// <summary>
/// Yes or no, answered with the probability of yes in a <see cref="YesNoAnswer"/>.
/// </summary>
/// <param name="Key">Identifies the question in the answers.</param>
/// <param name="Instructions">What is being asked.</param>
/// <param name="Yes">What a yes means, when the question alone does not say.</param>
/// <param name="No">What a no means, when the question alone does not say.</param>
public sealed record YesNoQuestion(string Key, string Instructions, string? Yes, string? No)
    : Question(Key, Instructions);

/// <summary>
/// One option or level: the name an answer uses, and what it means.
/// </summary>
public sealed record Criterion(string Name, string? Description);

/// <summary>
/// A decider's answers.
/// </summary>
/// <param name="Answers">One answer per question, keyed by <see cref="Question.Key"/>.</param>
public sealed record DecisionResult(IReadOnlyDictionary<string, Answer> Answers);

/// <summary>
/// One answer. <see cref="Model"/> says which model, at which version, gave it, because a
/// confidence threshold tuned against one version does not carry over to another.
/// </summary>
public abstract record Answer
{
    /// <summary>The model that answered, as the decider named it, or null for a decider that is not a model.</summary>
    public string? Model { get; init; }
}

/// <summary>
/// The chosen option, by <see cref="Criterion.Name"/>.
/// </summary>
/// <param name="Choice">The chosen option's name.</param>
/// <param name="Confidence">How sure the decider is, from 0 to 1.</param>
/// <param name="Probabilities">Each option's probability, by name, when the decider has them.</param>
public sealed record ChoiceAnswer(
    string Choice,
    double Confidence = 1.0,
    IReadOnlyDictionary<string, double>? Probabilities = null
) : Answer;

/// <summary>
/// A position on the levels, from 0 (the first level) to one less than the number of levels.
/// </summary>
/// <param name="Score">The position, which may fall between levels.</param>
/// <param name="Confidence">How sure the decider is, from 0 to 1.</param>
/// <param name="Probabilities">Each level's probability, in level order, when the decider has them.</param>
public sealed record ScoreAnswer(
    double Score,
    double Confidence = 1.0,
    IReadOnlyList<double>? Probabilities = null
) : Answer;

/// <summary>
/// The probability of yes. There is no separate confidence: the probability is the answer.
/// </summary>
public sealed record YesNoAnswer(double Probability) : Answer;
