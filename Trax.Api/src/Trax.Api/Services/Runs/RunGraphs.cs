using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Core.Monad;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Api.Services.Runs;

/// <summary>
/// Draws a run on its train's declared chain: the read behind <c>operations.runGraph</c> and the
/// run graph on the dashboard's run page, so the two show the same nodes in the same states.
/// </summary>
/// <remarks>
/// <para>The graph comes from <see cref="ITrainChainGraphs"/>, looked up by the run's train name,
/// so only a registered train is ever read. The steps come from <c>trax.junction_run</c> through
/// <see cref="JunctionRunQueries.ForRun"/> and <see cref="JunctionStep.From(Effect.Models.JunctionRun.JunctionRun)"/>,
/// the read and mapping <c>operations.junctionRuns</c> and the dashboard's timeline use.</para>
/// <para>A step is matched to a node by its <see cref="JunctionStep.NodeId"/> alone, never by its
/// name or position: two nodes can run the same junction, and a chain can change between a run and
/// the read.</para>
/// </remarks>
public static class RunGraphs
{
    /// <summary>
    /// The most steps one read matches, the largest page <c>operations.junctionRuns</c> returns.
    /// </summary>
    public const int MaxSteps = 500;

    /// <summary>
    /// Reads run <paramref name="metadataId"/> and its first <see cref="MaxSteps"/> steps, and
    /// matches them to its train's graph. Null when no run has the id.
    /// </summary>
    /// <param name="context">The data context to read the run and its steps from.</param>
    /// <param name="graphs">The registered trains' graphs.</param>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<RunGraph?> ReadAsync(
        IDataContext context,
        ITrainChainGraphs graphs,
        long metadataId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(graphs);

        var train = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (train is null)
            return null;

        // One more than the cap, so a run with more steps says so.
        var rows = await context
            .JunctionRuns.AsNoTracking()
            .ForRun(metadataId)
            .Take(MaxSteps + 1)
            .ToListAsync(cancellationToken);

        var more = rows.Count > MaxSteps;
        var steps = rows.Take(MaxSteps).Select(JunctionStep.From).ToList();

        return Match(metadataId, train, graphs.Find(train), steps, more);
    }

    /// <summary>
    /// Matches a run's steps to its train's graph. With no graph, every step is unmatched.
    /// </summary>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="train">The run's train name.</param>
    /// <param name="graph">The train's graph, or null when the host has none for it.</param>
    /// <param name="steps">The run's steps, in position order.</param>
    /// <param name="moreSteps">True when the run recorded more steps than <paramref name="steps"/> holds.</param>
    public static RunGraph Match(
        long metadataId,
        string train,
        ChainGraph? graph,
        IReadOnlyList<JunctionStep> steps,
        bool moreSteps = false
    )
    {
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(steps);

        if (graph is null)
            return new RunGraph(metadataId, train, false, null, [], steps.ToList(), moreSteps);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(graph.Nodes, ids);

        var byNode = new Dictionary<string, List<JunctionStep>>(StringComparer.Ordinal);
        var unmatched = new List<JunctionStep>();
        foreach (var step in steps)
        {
            if (step.NodeId is { } id && ids.Contains(id))
            {
                if (!byNode.TryGetValue(id, out var matched))
                    byNode[id] = matched = [];
                matched.Add(step);
            }
            else
                unmatched.Add(step);
        }

        return new RunGraph(
            metadataId,
            train,
            true,
            graph.Hash,
            Overlay(graph.Nodes, byNode, new Walk(), skipped: false),
            unmatched,
            moreSteps
        );
    }

    private static void CollectIds(IReadOnlyList<ChainGraphNode> nodes, HashSet<string> ids)
    {
        foreach (var node in nodes)
        {
            ids.Add(node.Id);
            foreach (var track in node.Tracks)
                CollectIds(track.Nodes, ids);
        }
    }

    private static List<RunGraphNode> Overlay(
        IReadOnlyList<ChainGraphNode> nodes,
        Dictionary<string, List<JunctionStep>> byNode,
        Walk walk,
        bool skipped
    ) => nodes.Select(node => Overlay(node, byNode, walk, skipped)).ToList();

    private static RunGraphNode Overlay(
        ChainGraphNode node,
        Dictionary<string, List<JunctionStep>> byNode,
        Walk walk,
        bool skipped
    )
    {
        IReadOnlyList<JunctionStep> steps = byNode.TryGetValue(node.Id, out var matched)
            ? matched
            : [];

        var state =
            steps.Count > 0 ? StateOf(steps)
            : skipped ? RunNodeState.Skipped
            : walk.Withheld ? RunNodeState.Withheld
            : node.Kind is ChainStepKind.Extract or ChainStepKind.Seed or ChainStepKind.Resolve
                ? RunNodeState.NotRecorded
            : RunNodeState.NotReached;

        var taken = node.Tracks.Count == 0 ? null : TrackTaken(node, steps, byNode);

        // A route whose answer is withheld withholds every step after it, on its tracks and past
        // them, because which steps ran would give the answer away. From here on a node with no
        // step says so rather than that the run never reached it.
        if (node.Tracks.Count > 0 && steps.Any(s => s.AnswerWithheld || s.NameWithheld))
            walk.Withheld = true;

        var tracks = node
            .Tracks.Select(track =>
            {
                var isTaken = taken is not null && track.Name == taken;
                // Once the track taken is known, every other track was passed over. Until then a
                // track's nodes stand as recorded, which is not reached for a track nothing ran on.
                var passedOver = skipped || (taken is not null && !isTaken);
                return new RunGraphTrack(
                    track.Name,
                    track.Description,
                    track.IsFallback,
                    isTaken,
                    Overlay(track.Nodes, byNode, walk, passedOver)
                );
            })
            .ToList();

        return new RunGraphNode(
            node.Id,
            node.Kind,
            node.Junction,
            node.In,
            node.Out,
            node.Opaque,
            state,
            steps.Any(s => s.Replayed),
            taken,
            steps,
            tracks
        );
    }

    /// <summary>What the walk over the graph, in declared order, has passed so far.</summary>
    private sealed class Walk
    {
        /// <summary>True once the walk has passed a route whose answer is withheld.</summary>
        public bool Withheld { get; set; }
    }

    // The worst state any of the node's steps reached, should a node have more than one: it fails
    // if any of them did.
    private static RunNodeState StateOf(IReadOnlyList<JunctionStep> steps) =>
        steps.Any(s => s.State == JunctionRunState.Failed) ? RunNodeState.Failed
        : steps.Any(s => s.State == JunctionRunState.Cancelled) ? RunNodeState.Cancelled
        : steps.Any(s => s.State == JunctionRunState.InProgress) ? RunNodeState.InProgress
        : RunNodeState.Completed;

    // The route's recorded answer names the track. When it is withheld, the one track holding
    // steps the run recorded is the one it took; with none, or more than one, it cannot be told.
    private static string? TrackTaken(
        ChainGraphNode node,
        IReadOnlyList<JunctionStep> steps,
        Dictionary<string, List<JunctionStep>> byNode
    )
    {
        var routed = steps.LastOrDefault(s =>
            s.Kind == JunctionRunKind.Route && s.Answer is not null
        );
        if (routed?.Answer is { } answer && node.Tracks.Any(t => t.Name == answer))
            return answer;

        var ran = node.Tracks.Where(t => AnyRecorded(t.Nodes, byNode)).Take(2).ToList();
        return ran.Count == 1 ? ran[0].Name : null;
    }

    private static bool AnyRecorded(
        IReadOnlyList<ChainGraphNode> nodes,
        Dictionary<string, List<JunctionStep>> byNode
    ) =>
        nodes.Any(n => byNode.ContainsKey(n.Id) || n.Tracks.Any(t => AnyRecorded(t.Nodes, byNode)));
}
