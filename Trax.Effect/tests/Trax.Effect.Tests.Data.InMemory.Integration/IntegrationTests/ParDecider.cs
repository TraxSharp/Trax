using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>Answers by the branch asking, in order, and counts what it was asked.</summary>
internal sealed class ParDecider : IDecider
{
    private Dictionary<string, Queue<ParLane>> _byBranch = [];
    private ParLane? _always;
    private int _asked;

    public int Asked => _asked;

    public void Script(Dictionary<string, ParLane[]>? byBranch = null, ParLane? always = null)
    {
        _byBranch = (byBranch ?? []).ToDictionary(
            b => b.Key,
            b => new Queue<ParLane>(b.Value),
            StringComparer.Ordinal
        );
        _always = always;
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);

        var branch = ChainGraph.CurrentBranchPath ?? "";
        ParLane lane;
        lock (_byBranch)
            lane =
                _byBranch.TryGetValue(branch, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : _always ?? ParLane.Ground;

        return new ScriptedDecider()
            .Choose(lane)
            .Choose(lane == ParLane.Express ? ParTier.Express : ParTier.Ground)
            .Decide(request, cancellationToken);
    }
}
