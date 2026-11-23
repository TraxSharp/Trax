using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>Answers each question with the next lane it was given, and counts what it was asked.</summary>
public sealed class InvokedLaneDecider : IDecider
{
    private readonly Queue<InvokedLane> _answers = new();
    private int _asked;

    public int Asked => _asked;

    public void Script(params InvokedLane[] answers)
    {
        lock (_answers)
        {
            _answers.Clear();
            foreach (var answer in answers)
                _answers.Enqueue(answer);
        }
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);
        InvokedLane lane;
        lock (_answers)
            lane = _answers.Dequeue();
        return new ScriptedDecider().Choose(lane).Decide(request, cancellationToken);
    }
}
