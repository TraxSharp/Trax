using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// One typed question as a train declares it: how to ask it, and how to turn the answer into the
/// typed decision Memory holds, refusing an answer that does not fit.
/// </summary>
internal abstract class QuestionSpec
{
    /// <summary>The key the question is asked under, unique within one request.</summary>
    public abstract string Key { get; }

    /// <summary>The enum or marker type the question is about.</summary>
    public abstract Type On { get; }

    /// <summary>The decision type the answer becomes in Memory.</summary>
    public abstract Type DecisionType { get; }

    /// <summary>What is wrong with the declaration, or null.</summary>
    public abstract string? Problem { get; }

    public abstract Question ToQuestion();

    /// <summary>The typed decision for <paramref name="answer"/>.</summary>
    /// <exception cref="InvalidAnswerException">The answer does not fit the question.</exception>
    public abstract object ToDecision(Answer answer);

    /// <summary>
    /// The name of the track a decision takes, when the step that asks this question also routes
    /// on it, or null for a <c>Decide</c> whose routing comes later. The function itself returns
    /// null for a decision that has no track to take.
    /// </summary>
    public Func<object, string?>? Route { get; init; }

    /// <summary>
    /// Whether a shadow's answer would have done what the live answer did, for comparing a shadow
    /// decider. When the step routes on the question, that means taking the same track, by the
    /// step's own bars and bands. A plain <c>Decide</c> knows nothing of the routing after it, so it
    /// compares the answers themselves (<see cref="SameAnswer"/>). A shadow answer that does not fit
    /// the question never agrees, and neither does one when the live answer has no track to take:
    /// two answers that both fail the run did not agree on anything the run did.
    /// </summary>
    public bool SameOutcome(Answer live, Answer shadow)
    {
        object liveDecision,
            shadowDecision;

        try
        {
            liveDecision = ToDecision(live);
            shadowDecision = ToDecision(shadow);
        }
        catch (InvalidAnswerException)
        {
            return false;
        }

        return Route is { } route
            ? route(liveDecision) is { } track
                && string.Equals(track, route(shadowDecision), StringComparison.Ordinal)
            : SameAnswer(liveDecision, shadowDecision);
    }

    /// <summary>Whether two typed decisions say the same thing, when no routing is known.</summary>
    protected abstract bool SameAnswer(object live, object shadow);

    /// <summary>
    /// Why an answer recorded by an earlier run cannot be replayed for this question as it is asked
    /// now, or null when it can. It has to fit exactly as a fresh answer does. The reason says what
    /// does not fit and never quotes the recorded answer, because it is logged and recorded, and
    /// which questions are sensitive is the host's to say.
    /// </summary>
    public string? ReplayProblem(Answer answer)
    {
        try
        {
            ToDecision(answer);
            return null;
        }
        catch (InvalidAnswerException invalid)
        {
            return invalid.ReplayReason;
        }
    }

    protected static string? Asks(Type type, string? asking) =>
        asking ?? type.GetCustomAttribute<AsksAttribute>()?.Question;

    /// <summary>
    /// What is wrong with the question about <paramref name="type"/> as it is worded and keyed,
    /// or null.
    /// </summary>
    protected static string? Unworded(Type type, string? instructions) =>
        instructions is null ? MissingQuestion(type) : QuestionKey.Problem(type);

    protected static string MissingQuestion(Type type) =>
        $"asks nothing about '{type.ReadableName()}'. A model sees only the question, never the "
        + "type's name: pass asking: or put [Asks(\"...\")] on the type.";

    protected static void CheckProbability(double value, string what)
    {
        if (double.IsNaN(value) || value is < 0 or > 1)
            throw new InvalidAnswerException(
                $"answered with {what} {Format(value)}, which is not between 0 and 1",
                "its recorded answer gives a confidence or probability that is not between 0 and 1"
            );
    }

    internal static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
