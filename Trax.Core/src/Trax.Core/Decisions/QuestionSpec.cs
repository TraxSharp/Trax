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
            ? route(liveDecision) is { } track && track == route(shadowDecision)
            : SameAnswer(liveDecision, shadowDecision);
    }

    /// <summary>Whether two typed decisions say the same thing, when no routing is known.</summary>
    protected abstract bool SameAnswer(object live, object shadow);

    /// <summary>
    /// Why an answer recorded by an earlier run cannot be replayed for this question as it is asked
    /// now, or null when it can. It has to fit exactly as a fresh answer does.
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
            return invalid.Message;
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
                $"answered with {what} {Format(value)}, which is not between 0 and 1"
            );
    }

    internal static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>An answer that does not fit its question. The run fails rather than act on it.</summary>
internal sealed class InvalidAnswerException(string message) : Exception(message);

/// <summary>Reads an enum's members, in order of their values, and the description each carries.</summary>
internal static class EnumMembers<T>
    where T : struct, Enum
{
    /// <summary>
    /// The members from the lowest value to the highest, as signed numbers: <c>-1</c> comes before
    /// <c>0</c>. <see cref="Enum.GetValues{TEnum}"/> orders by unsigned magnitude, which would put
    /// every negative member last.
    /// </summary>
    public static readonly IReadOnlyList<T> Ordered = Enum.GetValues<T>()
        .Distinct()
        .OrderBy(SignedValue)
        .ToList();

    private static decimal SignedValue(T value) =>
        Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture
            ),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };

    private static readonly Dictionary<string, T> ByName = Enum.GetNames<T>()
        .ToDictionary(n => n, Enum.Parse<T>, StringComparer.Ordinal);

    public static bool TryParse(string name, out T value) => ByName.TryGetValue(name, out value);

    public static string? Description(T value) =>
        typeof(T)
            .GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<DescriptionAttribute>()
            ?.Description;

    public static int IndexOf(T value)
    {
        for (var i = 0; i < Ordered.Count; i++)
            if (EqualityComparer<T>.Default.Equals(Ordered[i], value))
                return i;

        return -1;
    }
}

internal sealed class ChoiceSpec<TTrack>(
    string? asking,
    IReadOnlyList<(TTrack Option, string? Description)>? offered = null
) : QuestionSpec
    where TTrack : struct, Enum
{
    private readonly string? _instructions = Asks(typeof(TTrack), asking);

    private readonly IReadOnlyList<(TTrack Option, string? Description)> _offered =
        offered ?? EnumMembers<TTrack>.Ordered.Select(o => (o, (string?)null)).ToList();

    public override string Key => QuestionKey.For(typeof(TTrack));

    public override Type On => typeof(TTrack);

    public override Type DecisionType => typeof(ChoiceDecision<TTrack>);

    public override string? Problem =>
        Unworded(typeof(TTrack), _instructions) is { } unworded ? unworded
        : _offered.Count == 0 ? $"offers no options for '{typeof(TTrack).ReadableName()}'."
        : null;

    public override Question ToQuestion() =>
        new ChoiceQuestion(
            Key,
            _instructions!,
            _offered
                .Select(o => new Criterion(
                    o.Option.ToString(),
                    o.Description ?? EnumMembers<TTrack>.Description(o.Option)
                ))
                .ToList()
        );

    public override object ToDecision(Answer answer)
    {
        if (answer is not ChoiceAnswer choice)
            throw new InvalidAnswerException(
                $"answered '{Key}' with a {answer.GetType().Name}, not a choice"
            );

        // A name that is not a member cannot be represented, so it is refused. A member that was
        // not offered is kept: routing treats it as a choice it has no track for.
        if (!EnumMembers<TTrack>.TryParse(choice.Choice ?? "", out var chosen))
            throw new InvalidAnswerException(
                $"answered '{Key}' with '{choice.Choice}', which is not one of its options"
            );

        CheckProbability(choice.Confidence, "a confidence of");

        Dictionary<TTrack, double>? probabilities = null;

        if (choice.Probabilities is { } given)
        {
            probabilities = [];

            // Names the question did not offer are ignored: they say nothing about these options.
            foreach (var (name, p) in given)
                if (EnumMembers<TTrack>.TryParse(name, out var option))
                {
                    CheckProbability(p, $"a probability for '{name}' of");
                    probabilities[option] = p;
                }
        }

        return new ChoiceDecision<TTrack>(chosen, choice.Confidence, probabilities, choice.Model);
    }

    protected override bool SameAnswer(object live, object shadow) =>
        EqualityComparer<TTrack>.Default.Equals(
            ((ChoiceDecision<TTrack>)live).Choice,
            ((ChoiceDecision<TTrack>)shadow).Choice
        );
}

internal sealed class ScoreSpec<TLevel>(string? asking) : QuestionSpec
    where TLevel : struct, Enum
{
    private readonly string? _instructions = Asks(typeof(TLevel), asking);

    private static IReadOnlyList<TLevel> Levels => EnumMembers<TLevel>.Ordered;

    public override string Key => QuestionKey.For(typeof(TLevel));

    public override Type On => typeof(TLevel);

    public override Type DecisionType => typeof(ScoreDecision<TLevel>);

    public override string? Problem =>
        Unworded(typeof(TLevel), _instructions) is { } unworded ? unworded
        : Levels.Count < 2
            ? $"rates on '{typeof(TLevel).ReadableName()}', which has fewer than two levels."
        : null;

    public override Question ToQuestion() =>
        new ScoreQuestion(
            Key,
            _instructions!,
            Levels
                .Select(l => new Criterion(l.ToString(), EnumMembers<TLevel>.Description(l)))
                .ToList()
        );

    public override object ToDecision(Answer answer)
    {
        if (answer is not ScoreAnswer score)
            throw new InvalidAnswerException(
                $"answered '{Key}' with a {answer.GetType().Name}, not a score"
            );

        var top = Levels.Count - 1;

        if (double.IsNaN(score.Score) || score.Score < 0 || score.Score > top)
            throw new InvalidAnswerException(
                $"scored '{Key}' at {Format(score.Score)}, outside its levels 0 to {top}"
            );

        CheckProbability(score.Confidence, "a confidence of");

        Dictionary<TLevel, double>? probabilities = null;

        if (score.Probabilities is { } given)
        {
            if (given.Count != Levels.Count)
                throw new InvalidAnswerException(
                    $"gave {given.Count} probabilities for '{Key}', which has {Levels.Count} levels"
                );

            probabilities = [];

            for (var i = 0; i < given.Count; i++)
            {
                CheckProbability(given[i], $"a probability for '{Levels[i]}' of");
                probabilities[Levels[i]] = given[i];
            }
        }

        return new ScoreDecision<TLevel>(
            score.Score,
            Levels[Nearest(score.Score)],
            score.Confidence,
            probabilities,
            score.Model
        );
    }

    /// <summary>The level a score rounds to, halves rounding up.</summary>
    internal static int Nearest(double score) => (int)Math.Floor(score + 0.5);

    protected override bool SameAnswer(object live, object shadow) =>
        EqualityComparer<TLevel>.Default.Equals(
            ((ScoreDecision<TLevel>)live).Nearest,
            ((ScoreDecision<TLevel>)shadow).Nearest
        );
}

internal sealed class YesNoSpec<TQuestion>(string? asking, string? yes, string? no) : QuestionSpec
{
    private static readonly AsksAttribute? Attribute =
        typeof(TQuestion).GetCustomAttribute<AsksAttribute>();

    private readonly string? _instructions = Asks(typeof(TQuestion), asking);

    public override string Key => QuestionKey.For(typeof(TQuestion));

    public override Type On => typeof(TQuestion);

    public override Type DecisionType => typeof(YesNoDecision<TQuestion>);

    public override string? Problem => Unworded(typeof(TQuestion), _instructions);

    public override Question ToQuestion() =>
        new YesNoQuestion(Key, _instructions!, yes ?? Attribute?.Yes, no ?? Attribute?.No);

    public override object ToDecision(Answer answer)
    {
        if (answer is not YesNoAnswer yesNo)
            throw new InvalidAnswerException(
                $"answered '{Key}' with a {answer.GetType().Name}, not a yes/no"
            );

        CheckProbability(yesNo.Probability, "a probability of");

        return new YesNoDecision<TQuestion>(yesNo.Probability, yesNo.Model);
    }

    protected override bool SameAnswer(object live, object shadow) =>
        ((YesNoDecision<TQuestion>)live).Probability >= 0.5
        == ((YesNoDecision<TQuestion>)shadow).Probability >= 0.5;
}
