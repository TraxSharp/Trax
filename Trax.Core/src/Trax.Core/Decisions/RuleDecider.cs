using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// A decider written as plain rules, for decisions that are policy rather than judgement.
/// </summary>
/// <remarks>
/// Each rule answers one question, identified by the type the question is about, and is always
/// sure of its answer. A question with no rule, or a state of a different type than its rule
/// takes, throws, which fails the run: a rule table that does not cover a question has not decided
/// it.
/// <code>
/// new RuleDecider()
///     .Choice&lt;LoanApplication, Underwriting&gt;(a =&gt; a.CreditScore &gt;= 740 ? Underwriting.Approve : Underwriting.Review)
///     .YesNo&lt;Post, ContainsThreat&gt;(p =&gt; p.Body.Contains("kill"));
/// </code>
/// </remarks>
public sealed class RuleDecider : IDecider
{
    private readonly Dictionary<string, Func<object, Answer>> _rules = [];

    /// <summary>Answers the choice question about <typeparamref name="TTrack"/>.</summary>
    public RuleDecider Choice<TState, TTrack>(Func<TState, TTrack> rule)
        where TTrack : struct, Enum =>
        Add<TState>(typeof(TTrack), state => new ChoiceAnswer(rule(state).ToString()));

    /// <summary>Answers the scale question about <typeparamref name="TLevel"/> with a level.</summary>
    public RuleDecider Score<TState, TLevel>(Func<TState, TLevel> rule)
        where TLevel : struct, Enum =>
        Add<TState>(
            typeof(TLevel),
            state => new ScoreAnswer(EnumMembers<TLevel>.IndexOf(rule(state)))
        );

    /// <summary>Answers the yes/no question <typeparamref name="TQuestion"/>.</summary>
    public RuleDecider YesNo<TState, TQuestion>(Func<TState, bool> rule) =>
        Add<TState>(typeof(TQuestion), state => new YesNoAnswer(rule(state) ? 1.0 : 0.0));

    /// <inheritdoc />
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, Answer>();

        foreach (var question in request.Questions)
        {
            if (!_rules.TryGetValue(question.Key, out var rule))
                throw new InvalidOperationException(
                    $"RuleDecider has no rule for the question '{question.Key}'. Add one with "
                        + "Choice, Score or YesNo."
                );

            answers[question.Key] = rule(request.State);
        }

        return Task.FromResult(new DecisionResult(answers));
    }

    private RuleDecider Add<TState>(Type about, Func<TState, Answer> rule)
    {
        var key = QuestionKey.For(about);

        if (_rules.ContainsKey(key))
            throw new ArgumentException(
                $"RuleDecider already has a rule for '{about.ReadableName()}'.",
                nameof(about)
            );

        _rules[key] = state =>
            state is TState typed
                ? rule(typed)
                : throw new InvalidOperationException(
                    $"RuleDecider's rule for '{about.ReadableName()}' takes a "
                        + $"'{typeof(TState).ReadableName()}', but the question was asked about a "
                        + $"'{state.GetType().ReadableName()}'."
                );

        return this;
    }
}
