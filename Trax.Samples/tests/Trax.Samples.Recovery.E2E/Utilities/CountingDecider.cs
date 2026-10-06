using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Samples.Recovery.Model;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.Research;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// The demo model's answers, at once, counting how many times each run's questions were put to it.
/// A replayed question never reaches a decider, so the count is how many times the model was paid for.
/// </summary>
public sealed class CountingDecider : IDecider
{
    private readonly ConcurrentDictionary<(string RunId, string Key), int> _asked = new();

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        var runId = request.State switch
        {
            ResearchBrief brief => brief.RunId,
            Findings findings => findings.RunId,
            RefundCase refund => refund.RunId,
            _ => "unknown",
        };

        foreach (var question in request.Questions)
            _asked.AddOrUpdate((runId, question.Key), 1, (_, count) => count + 1);

        return Task.FromResult(DemoDecider.Answer(request));
    }

    /// <summary>How many times the run <paramref name="runId"/> asked the question <paramref name="key"/>.</summary>
    public int Asked(string runId, string key) => _asked.GetValueOrDefault((runId, key));
}
