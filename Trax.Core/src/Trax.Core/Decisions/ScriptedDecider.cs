namespace Trax.Core.Decisions;

/// <summary>
/// A decider whose answers are written in advance, for testing a train's decisions without a
/// model, and for running one locally before a model is wired up.
/// </summary>
/// <remarks>
/// It keeps every request it is asked, so a test can check exactly what a model would have been
/// shown. A question it has no answer for is left unanswered, which fails the run the way a model
/// that skipped a question would.
/// </remarks>
public sealed class ScriptedDecider : IDecider
{
    private readonly Dictionary<string, Func<DecisionRequest, Answer?>> _answers = [];

    private readonly List<DecisionRequest> _requests = [];

    private readonly Lock _gate = new();

    private CancellationToken _lastToken;

    private Exception? _failure;

    /// <summary>
    /// Every request asked so far, in order, as a copy taken when it is read. It is safe to read
    /// while the decider is being asked from other threads, as a shadow is.
    /// </summary>
    public IReadOnlyList<DecisionRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>The cancellation token of the most recent request.</summary>
    public CancellationToken LastToken
    {
        get
        {
            lock (_gate)
                return _lastToken;
        }
    }

    /// <summary>Answers the choice about <typeparamref name="TTrack"/> with <paramref name="choice"/>.</summary>
    public ScriptedDecider Choose<TTrack>(
        TTrack choice,
        double confidence = 1.0,
        IReadOnlyDictionary<TTrack, double>? probabilities = null,
        string? model = null
    )
        where TTrack : struct, Enum =>
        Answer(
            QuestionKey.For<TTrack>(),
            _ => new ChoiceAnswer(
                choice.ToString(),
                confidence,
                probabilities?.ToDictionary(p => p.Key.ToString(), p => p.Value)
            )
            {
                Model = model,
            }
        );

    /// <summary>Answers the scale question about <typeparamref name="TLevel"/> with <paramref name="score"/>.</summary>
    public ScriptedDecider Score<TLevel>(
        double score,
        double confidence = 1.0,
        string? model = null
    )
        where TLevel : struct, Enum =>
        Answer(
            QuestionKey.For<TLevel>(),
            _ => new ScoreAnswer(score, confidence) { Model = model }
        );

    /// <summary>Answers the yes/no question <typeparamref name="TQuestion"/> with <paramref name="probability"/>.</summary>
    public ScriptedDecider YesNo<TQuestion>(double probability, string? model = null) =>
        Answer(QuestionKey.For<TQuestion>(), _ => new YesNoAnswer(probability) { Model = model });

    /// <summary>
    /// Answers the question <paramref name="key"/> however <paramref name="answer"/> says, including
    /// with an answer that does not fit, to test how a train refuses one.
    /// </summary>
    /// <param name="key">The question's key, which <see cref="QuestionKey.For{T}"/> gives.</param>
    /// <param name="answer">Makes the answer from the request.</param>
    public ScriptedDecider Answer(string key, Func<DecisionRequest, Answer?> answer)
    {
        _answers[key] = answer;
        return this;
    }

    /// <summary>Fails every request with <paramref name="failure"/>, as an unavailable model would.</summary>
    public ScriptedDecider Throws(Exception failure)
    {
        _failure = failure;
        return this;
    }

    /// <inheritdoc />
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(request);
            _lastToken = cancellationToken;
        }

        if (_failure is not null)
            return Task.FromException<DecisionResult>(_failure);

        var answers = new Dictionary<string, Answer>();

        foreach (var question in request.Questions)
            if (_answers.GetValueOrDefault(question.Key)?.Invoke(request) is { } answer)
                answers[question.Key] = answer;

        return Task.FromResult(new DecisionResult(answers));
    }
}
