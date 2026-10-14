using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Samples.Recovery.Index;
using Trax.Samples.Recovery.Model;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.Topics;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// The demo model's answers, at once, counting how many times each run's questions were put to it.
/// A replayed question never reaches a decider, so the count is how many times the model was paid for.
/// An ingest's questions are counted under its partition key, <c>Source/yyyy-MM</c>.
/// </summary>
public sealed class CountingDecider : IDecider
{
    private readonly ConcurrentDictionary<(string RunId, string Key), int> _asked = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _holds = new();

    public async Task<DecisionResult> Decide(
        DecisionRequest request,
        CancellationToken cancellationToken
    )
    {
        var runId = request.State switch
        {
            RefundCase refund => refund.RunId,
            RefundEvidence evidence => evidence.RunId,
            CoCitationEvidence evidence => evidence.RunId,
            MatchEvidence evidence => IndexFixture.PartitionKey(evidence.Source, evidence.Month),
            _ => "unknown",
        };

        foreach (var question in request.Questions)
            _asked.AddOrUpdate((runId, question.Key), 1, (_, count) => count + 1);

        foreach (var question in request.Questions)
            if (_holds.TryGetValue(question.Key, out var hold))
                await hold.Task.WaitAsync(cancellationToken);

        return DemoDecider.Answer(request);
    }

    /// <summary>
    /// Keeps every run that asks the question <paramref name="key"/> waiting on it until the returned
    /// hold is disposed, so a test can act while a run is at a known point. The question is counted
    /// when it is asked, not when the hold lets it through.
    /// </summary>
    public IDisposable Hold(string key)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _holds[key] = hold;
        return new Release(() =>
        {
            _holds.TryRemove(new KeyValuePair<string, TaskCompletionSource>(key, hold));
            hold.TrySetResult();
        });
    }

    /// <summary>How many times the run <paramref name="runId"/> asked the question <paramref name="key"/>.</summary>
    public int Asked(string runId, string key) => _asked.GetValueOrDefault((runId, key));

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
