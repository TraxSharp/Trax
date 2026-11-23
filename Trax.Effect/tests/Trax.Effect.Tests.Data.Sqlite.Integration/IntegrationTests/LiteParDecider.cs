using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>Answers by the branch asking, in order, Ground once a branch's script runs out.</summary>
internal sealed class LiteParDecider : IDecider
{
    private Dictionary<string, Queue<LiteParLane>> _byBranch = [];
    private int _asked;

    public int Asked => _asked;

    public void Script(Dictionary<string, LiteParLane[]> byBranch)
    {
        _byBranch = byBranch.ToDictionary(
            b => b.Key,
            b => new Queue<LiteParLane>(b.Value),
            StringComparer.Ordinal
        );
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);

        var branch = ChainGraph.CurrentBranchPath ?? "";
        LiteParLane lane;
        lock (_byBranch)
            lane =
                _byBranch.TryGetValue(branch, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : LiteParLane.Ground;

        return new ScriptedDecider().Choose(lane).Decide(request, cancellationToken);
    }
}
