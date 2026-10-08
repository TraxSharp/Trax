using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Core.Monad;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.Checkpoints;
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
    public static Task<RunGraph?> ReadAsync(
        IDataContext context,
        ITrainChainGraphs graphs,
        long metadataId,
        CancellationToken cancellationToken
    ) => ReadAsync(context, graphs, null, metadataId, cancellationToken);

    /// <summary>
    /// Reads run <paramref name="metadataId"/> and its first <see cref="MaxSteps"/> steps, matches
    /// them to its train's graph, and, through <paramref name="resumes"/>, says where the run can
    /// resume, which nodes hold its checkpoints and, for a resumed run, which nodes it restored.
    /// Null when no run has the id.
    /// </summary>
    /// <param name="context">The data context to read the run and its steps from.</param>
    /// <param name="graphs">The registered trains' graphs.</param>
    /// <param name="resumes">The resume check, or null to read no resumes or checkpoints.</param>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<RunGraph?> ReadAsync(
        IDataContext context,
        ITrainChainGraphs graphs,
        IRunResumes? resumes,
        long metadataId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(graphs);

        var run = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => new
            {
                m.Name,
                m.TrainState,
                m.InvokingMachine,
                m.ResumeFrom,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
            return null;

        // One more than the cap, so a run with more steps says so.
        var rows = await context
            .JunctionRuns.AsNoTracking()
            .ForRun(metadataId)
            .Take(MaxSteps + 1)
            .ToListAsync(cancellationToken);

        var more = rows.Count > MaxSteps;
        var steps = rows.Take(MaxSteps).Select(JunctionStep.From).ToList();
        var graph = graphs.Find(run.Name);

        var checks = ReadsResumes(run.TrainState, run.ResumeFrom)
            ? await ReadResumesAsync(
                resumes,
                graphs,
                metadataId,
                run.Name,
                graph,
                cancellationToken
            )
            : null;

        return Match(
            metadataId,
            run.Name,
            graph,
            steps,
            more,
            checks,
            Resumable(run.TrainState, run.InvokingMachine)
        );
    }

    /// <summary>
    /// True when a run in <paramref name="state"/> has resumes or checkpoints worth reading: it
    /// failed or was cancelled, so it may resume, or it is itself a resumed run, which restored
    /// nodes. A run that completed keeps no checkpoints, and one still going cannot resume yet.
    /// </summary>
    /// <param name="state">The run's state.</param>
    /// <param name="resumeFrom">The run it resumed, or null.</param>
    public static bool ReadsResumes(TrainState state, long? resumeFrom) =>
        state is TrainState.Failed or TrainState.Cancelled || resumeFrom is not null;

    /// <summary>
    /// True when an operator may resume a run in <paramref name="state"/>: it failed or was
    /// cancelled, and no state machine's step started it, whose outcome only that step receives
    /// (central ADR 0046).
    /// </summary>
    /// <param name="state">The run's state.</param>
    /// <param name="invokingMachine">The machine whose step started it, or null.</param>
    public static bool Resumable(TrainState state, string? invokingMachine) =>
        state is TrainState.Failed or TrainState.Cancelled && invokingMachine is null;

    /// <summary>
    /// <see cref="Resumable(TrainState, string)"/> for a run read whole, as the dashboard's run page
    /// reads it.
    /// </summary>
    /// <param name="run">The run.</param>
    public static bool Resumable(Effect.Models.Metadata.Metadata run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Resumable(run.TrainState, run.InvokingMachine);
    }

    /// <summary>
    /// Asks <paramref name="resumes"/>, with one read of the run's checkpoint lineage, whether run
    /// <paramref name="metadataId"/> can resume after its latest checkpoint and at each node of
    /// <paramref name="graph"/>, which nodes hold its checkpoints, and which it restored. Null when
    /// there is no check, no graph, or the train's declared chain cannot be read here. It names
    /// nodes only, never what a checkpoint holds.
    /// </summary>
    /// <param name="resumes">The resume check, or null.</param>
    /// <param name="graphs">The registered trains' graphs and declared chains.</param>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="train">The run's train name.</param>
    /// <param name="graph">The train's graph, or null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<ResumeChecks?> ReadResumesAsync(
        IRunResumes? resumes,
        ITrainChainGraphs graphs,
        long metadataId,
        string train,
        ChainGraph? graph,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(graphs);

        if (resumes is null || graph is null || graphs.FindDeclared(train) is not { } declared)
            return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(graph.Nodes, ids);

        return await resumes.CheckMany(
            declared.Train,
            declared.Chain,
            declared.Input,
            declared.Output,
            metadataId,
            ids,
            cancellationToken
        );
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
    ) => Match(metadataId, train, graph, steps, moreSteps, null, false);

    /// <summary>
    /// Matches a run's steps to its train's graph, and places what <paramref name="resumes"/>
    /// read on it: which nodes hold a checkpoint, which the run restored rather than ran because
    /// it resumed, and, when <paramref name="resumable"/>, where it can resume. With no graph,
    /// every step is unmatched.
    /// </summary>
    /// <param name="metadataId">The run's id.</param>
    /// <param name="train">The run's train name.</param>
    /// <param name="graph">The train's graph, or null when the host has none for it.</param>
    /// <param name="steps">The run's steps, in position order.</param>
    /// <param name="moreSteps">True when the run recorded more steps than <paramref name="steps"/> holds.</param>
    /// <param name="resumes">What <see cref="ReadResumesAsync"/> read, or null.</param>
    /// <param name="resumable">
    /// True when an operator may resume the run (<see cref="Resumable(TrainState, string)"/>); otherwise no node offers
    /// a resume, whatever the check says.
    /// </param>
    public static RunGraph Match(
        long metadataId,
        string train,
        ChainGraph? graph,
        IReadOnlyList<JunctionStep> steps,
        bool moreSteps,
        ResumeChecks? resumes,
        bool resumable
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

        var resume = new ResumeView(resumes, resumable && resumes is not null);

        return new RunGraph(
            metadataId,
            train,
            true,
            graph.Hash,
            Overlay(graph.Nodes, byNode, new Walk { Resume = resume }, skipped: false),
            unmatched,
            moreSteps
        )
        {
            CanResume = resume.Offers && resumes!.Latest.CanResume,
        };
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
        if (node.Kind == ChainStepKind.Parallel)
            return OverlayParallel(node, byNode, walk, skipped);

        IReadOnlyList<JunctionStep> steps = byNode.TryGetValue(node.Id, out var matched)
            ? matched
            : [];

        // A checkpoint records no step of its own: the row it stored says the run reached it,
        // unless a withheld route came before it, which the walk keeps withholding.
        var state =
            steps.Count > 0 ? StateOf(steps)
            : skipped ? RunNodeState.Skipped
            : walk.Resume.Restored(node.Id) ? RunNodeState.Restored
            : walk.Withheld ? RunNodeState.Withheld
            : walk.Resume.Wrote(node.Id) ? RunNodeState.Completed
            : node.Kind
                is ChainStepKind.Extract
                    or ChainStepKind.Seed
                    or ChainStepKind.Resolve
                    or ChainStepKind.Checkpoint
                ? RunNodeState.NotRecorded
            : RunNodeState.NotReached;

        // A resumed run recorded no step for a routing step before its point; the checkpoint it
        // restored says which track that step took.
        var taken =
            node.Tracks.Count == 0
                ? null
                : TrackTaken(node, steps, byNode) ?? walk.Resume.TakenBefore(node.Id);

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
        )
        {
            CanResume = walk.Resume.CanResumeAt(node.Id),
            Checkpointed = walk.Resume.Holds(node.Id),
        };
    }

    /// <summary>
    /// A <c>Parallel</c> step: unlike a routing step's tracks, every branch runs, so none is passed
    /// over and each branch's nodes stand as the run recorded them. The step records nothing of its
    /// own, so where it stands is read from its branches.
    /// </summary>
    /// <remarks>
    /// Each branch is walked on its own: a withheld route inside one branch withholds that branch's
    /// later nodes, which is the path its steps' withheld ids came from, and not its siblings'.
    /// Past the join every branch has run, so the walk after the step is withheld when any branch's
    /// was.
    /// </remarks>
    private static RunGraphNode OverlayParallel(
        ChainGraphNode node,
        Dictionary<string, List<JunctionStep>> byNode,
        Walk walk,
        bool skipped
    )
    {
        IReadOnlyList<JunctionStep> steps = byNode.TryGetValue(node.Id, out var matched)
            ? matched
            : [];

        var withheldBefore = walk.Withheld;
        var branches = new List<(ChainGraphTrack Branch, List<RunGraphNode> Nodes)>();
        foreach (var branch in node.Tracks)
        {
            var path = new Walk { Withheld = withheldBefore, Resume = walk.Resume };
            branches.Add((branch, Overlay(branch.Nodes, byNode, path, skipped)));
            walk.Withheld |= path.Withheld;
        }

        var state = skipped
            ? RunNodeState.Skipped
            : ParallelStateOf(steps, branches.Select(b => b.Nodes).ToList(), withheldBefore);

        // A resumed run that skipped the whole step ran none of its branches.
        if (state == RunNodeState.NotReached && walk.Resume.Restored(node.Id))
            state = RunNodeState.Restored;

        // Every branch runs once the step does; before it does, or when the step sits on a track
        // the run did not take, none has.
        var started = Started(state);

        return new RunGraphNode(
            node.Id,
            node.Kind,
            node.Junction,
            node.In,
            node.Out,
            node.Opaque,
            state,
            steps.Any(s => s.Replayed),
            null,
            steps,
            branches
                .Select(b => new RunGraphTrack(
                    b.Branch.Name,
                    b.Branch.Description,
                    b.Branch.IsFallback,
                    started,
                    b.Nodes
                ))
                .ToList()
        )
        {
            CanResume = walk.Resume.CanResumeAt(node.Id),
            Checkpointed = walk.Resume.Holds(node.Id),
        };
    }

    // Where a Parallel step stands, from its branches: failed when any branch failed, cancelled
    // when one was (a sibling's failure or the run's cancel stopped it), running while any node is
    // running or any branch has a node still to reach, completed once every branch has, and not
    // reached when no branch recorded anything.
    private static RunNodeState ParallelStateOf(
        IReadOnlyList<JunctionStep> steps,
        IReadOnlyList<List<RunGraphNode>> branches,
        bool withheldBefore
    )
    {
        var states = branches
            .SelectMany(Descendants)
            .Select(n => n.State)
            .Concat(steps.Count > 0 ? [StateOf(steps)] : [])
            .ToList();

        if (!states.Any(Started))
            return withheldBefore ? RunNodeState.Withheld : RunNodeState.NotReached;

        if (states.Contains(RunNodeState.Failed))
            return RunNodeState.Failed;
        if (states.Contains(RunNodeState.Cancelled))
            return RunNodeState.Cancelled;
        if (states.Contains(RunNodeState.InProgress))
            return RunNodeState.InProgress;

        // A branch is done when none of its own nodes is still to reach. A node on a track it did
        // not take is skipped, one that records nothing says so, and one past a withheld route
        // cannot be told, so none of them holds the step open.
        return branches.All(b => b.All(n => n.State != RunNodeState.NotReached))
            ? RunNodeState.Completed
            : RunNodeState.InProgress;
    }

    // A state only a recorded step gives a node.
    private static bool Started(RunNodeState state) =>
        state
            is RunNodeState.InProgress
                or RunNodeState.Completed
                or RunNodeState.Failed
                or RunNodeState.Cancelled;

    private static IEnumerable<RunGraphNode> Descendants(IEnumerable<RunGraphNode> nodes) =>
        nodes.SelectMany(n => n.Tracks.SelectMany(t => Descendants(t.Nodes)).Prepend(n));

    /// <summary>What the walk over the graph, in declared order, has passed so far.</summary>
    private sealed class Walk
    {
        /// <summary>True once the walk has passed a route whose answer is withheld.</summary>
        public bool Withheld { get; set; }

        /// <summary>Where the run can resume and what it restored, the same for every path.</summary>
        public ResumeView Resume { get; init; } = ResumeView.None;
    }

    /// <summary>
    /// What <see cref="IRunResumes.CheckMany"/> read about a run, as each node asks it: node ids
    /// only, never a checkpoint's state or tracks.
    /// </summary>
    /// <param name="checks">What was read, or null when nothing was.</param>
    /// <param name="offers">True when the run may be resumed, so a node's verdict is shown.</param>
    private sealed class ResumeView(ResumeChecks? checks, bool offers)
    {
        public static readonly ResumeView None = new(null, false);

        private readonly HashSet<string> _restored = new(
            checks?.Restored ?? [],
            StringComparer.Ordinal
        );

        private readonly HashSet<string> _written = new(
            checks?.Written ?? [],
            StringComparer.Ordinal
        );

        private readonly HashSet<string> _checkpoints = new(
            checks?.Checkpoints ?? [],
            StringComparer.Ordinal
        );

        /// <summary>True when the run may be resumed and a check was read.</summary>
        public bool Offers => offers;

        public bool Restored(string id) => _restored.Contains(id);

        /// <summary>The track a routing step the resumed run restored took, or null.</summary>
        public string? TakenBefore(string id) =>
            checks?.RestoredTracks.GetValueOrDefault(id) is { } track && Restored(id)
                ? track
                : null;

        public bool Wrote(string id) => _written.Contains(id);

        public bool Holds(string id) => _checkpoints.Contains(id);

        public bool CanResumeAt(string id) =>
            offers && checks!.At.TryGetValue(id, out var verdict) && verdict.CanResume;
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
