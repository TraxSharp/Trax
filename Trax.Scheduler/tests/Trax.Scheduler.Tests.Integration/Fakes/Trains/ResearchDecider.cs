using System.Collections.Concurrent;
using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Answers the research train's questions with the choices set for them.</summary>
public sealed class ResearchDecider : IDecider
{
    private static readonly TimeSpan HoldBound = TimeSpan.FromSeconds(30);

    private int _asked;
    private bool _holdNext;
    private TaskCompletionSource _released = NewGate();

    public ResearchDepth Depth { get; set; } = ResearchDepth.Deep;

    public SummaryStyle Style { get; set; } = SummaryStyle.Long;

    public int Asked => _asked;

    /// <summary>Released once when a held question is asked: the run is between two junctions.</summary>
    public SemaphoreSlim Asking { get; private set; } = new(0);

    /// <summary>Makes the next question wait, once asked, until <see cref="Release"/>.</summary>
    public void HoldNextAsk() => _holdNext = true;

    /// <summary>Lets the held question be answered.</summary>
    public void Release() => _released.TrySetResult();

    public void Reset()
    {
        Depth = ResearchDepth.Deep;
        Style = SummaryStyle.Long;
        Interlocked.Exchange(ref _asked, 0);
        _holdNext = false;
        _released.TrySetResult();
        _released = NewGate();
        Asking = new SemaphoreSlim(0);
    }

    public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);

        if (_holdNext)
        {
            _holdNext = false;
            Asking.Release();
            await _released.Task.WaitAsync(HoldBound, ct).ConfigureAwait(false);
        }

        return new DecisionResult(
            new Dictionary<string, Answer>(StringComparer.Ordinal)
            {
                [QuestionKey.For<ResearchDepth>()] = new ChoiceAnswer(Depth.ToString()),
                [QuestionKey.For<SummaryStyle>()] = new ChoiceAnswer(Style.ToString()),
            }
        );
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
