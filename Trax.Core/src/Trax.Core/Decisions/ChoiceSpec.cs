using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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
                $"answered '{Key}' with a {answer.GetType().Name}, not a choice",
                "its recorded answer is not a choice"
            );

        // A name that is not a member cannot be represented, so it is refused. A member that was
        // not offered is kept: routing treats it as a choice it has no track for.
        if (!EnumMembers<TTrack>.TryParse(choice.Choice ?? "", out var chosen))
            throw new InvalidAnswerException(
                $"answered '{Key}' with '{choice.Choice}', which is not one of its options",
                "its recorded answer is not one of its options"
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
