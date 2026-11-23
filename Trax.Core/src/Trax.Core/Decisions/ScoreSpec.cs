using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Extensions;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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
            _instructions.NotNull(),
            Levels
                .Select(l => new Criterion(l.ToString(), EnumMembers<TLevel>.Description(l)))
                .ToList()
        );

    public override object ToDecision(Answer answer)
    {
        if (answer is not ScoreAnswer score)
            throw new InvalidAnswerException(
                $"answered '{Key}' with a {answer.GetType().Name}, not a score",
                "its recorded answer is not a score"
            );

        var top = Levels.Count - 1;

        if (double.IsNaN(score.Score) || score.Score < 0 || score.Score > top)
            throw new InvalidAnswerException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"scored '{Key}' at {Format(score.Score)}, outside its levels 0 to {top}"
                ),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"its recorded score is outside its levels 0 to {top}"
                )
            );

        CheckProbability(score.Confidence, "a confidence of");

        Dictionary<TLevel, double>? probabilities = null;

        if (score.Probabilities is { } given)
        {
            if (given.Count != Levels.Count)
                throw new InvalidAnswerException(
                    $"gave {given.Count} probabilities for '{Key}', which has {Levels.Count} levels",
                    $"its recorded answer does not give one probability for each of its {Levels.Count} levels"
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
