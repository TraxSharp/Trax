using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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
                $"answered '{Key}' with a {answer.GetType().Name}, not a yes/no",
                "its recorded answer is not a yes/no"
            );

        CheckProbability(yesNo.Probability, "a probability of");

        return new YesNoDecision<TQuestion>(yesNo.Probability, yesNo.Model);
    }

    protected override bool SameAnswer(object live, object shadow) =>
        ((YesNoDecision<TQuestion>)live).Probability >= 0.5
        == ((YesNoDecision<TQuestion>)shadow).Probability >= 0.5;
}
