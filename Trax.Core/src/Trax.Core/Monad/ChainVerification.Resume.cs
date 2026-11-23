using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

public static partial class ChainVerification
{
    /// <summary>
    /// Decides, without running anything, whether a run of the chain can resume at
    /// <paramref name="resumeAt"/>, or after its latest checkpoint when that is null, given the
    /// checkpoints the earlier run wrote.
    /// </summary>
    /// <remarks>
    /// A forward walk from the resume point over the declared chain: every step from there on
    /// must find its inputs in what the checkpoint restores, the train's input, a value handed to
    /// the chain, the container, or what a step after the point produces. Steps before the point
    /// are skipped as a resumed run skips them, so a type produced after the step that needs it
    /// does not count.
    /// </remarks>
    /// <param name="chain">The train's declared chain.</param>
    /// <param name="input">The train's input type.</param>
    /// <param name="output">The train's return type.</param>
    /// <param name="availableElsewhere">Whether the container supplies a type.</param>
    /// <param name="written">The ids of the checkpoints the earlier run, and those it resumed, wrote.</param>
    /// <param name="resumeAt">The step to resume at, or null for after the latest checkpoint.</param>
    /// <param name="withheld">
    /// Whether the store withholds the route a routing step on this key took: a checkpoint holds no
    /// <see cref="TrackTaken{TKey}"/> for it, so a resume does not restore one. Null when the store
    /// keeps every route.
    /// </param>
    internal static ResumeOutcome CheckResume(
        ChainRecorder chain,
        Type input,
        Type output,
        Func<Type, bool>? availableElsewhere,
        IReadOnlyCollection<string> written,
        string? resumeAt,
        Func<Type, bool>? withheld = null
    )
    {
        var nodes = Walk(chain);
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        var checkpoints = nodes
            .Where(n =>
                n.Step.Kind == ChainStepKind.Checkpoint
                && written.Contains(n.Id, StringComparer.Ordinal)
            )
            .ToList();

        var latestInBranch = checkpoints
            .Where(n => n.BranchPath is not null)
            .GroupBy(n => n.BranchPath.NotNull(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(n => n.Order).NotNull(), StringComparer.Ordinal);

        var start = resumeAt is null
            ? StartAfterLatestCheckpoint(nodes, byId, checkpoints, latestInBranch)
            : StartAtStep(nodes, byId, checkpoints, latestInBranch, resumeAt);

        if (start.Refusal is { } refused)
            return refused;

        return WalkFromStart(chain, input, output, availableElsewhere, withheld, nodes, start);
    }

    /// <summary>
    /// Where a resume starts: the main checkpoint it restores, the branch checkpoints it restores,
    /// the step it resumes at and whether that step runs again. <see cref="Refusal"/> is set
    /// instead when it cannot start at all.
    /// </summary>
    private sealed record ResumeStart(
        ResumeOutcome? Refusal,
        Node? Main,
        Dictionary<string, Node> BranchCheckpoints,
        string? Target,
        bool Inclusive
    )
    {
        public static ResumeStart Refused(ResumeOutcome refusal) =>
            new(refusal, Main: null, [], Target: null, Inclusive: false);
    }

    private static ResumeStart StartAfterLatestCheckpoint(
        List<Node> nodes,
        Dictionary<string, Node> byId,
        List<Node> checkpoints,
        Dictionary<string, Node> latestInBranch
    )
    {
        var main = checkpoints.Where(n => n.BranchPath is null).MaxBy(n => n.Order);
        var branchCheckpoints = BranchesThatHold(nodes, byId, latestInBranch, main, point: null);

        if (main is null && branchCheckpoints.Count == 0)
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.NoCheckpoint,
                    "The run wrote no checkpoint, so it can only run again from the top."
                )
            );

        return new ResumeStart(Refusal: null, main, branchCheckpoints, main?.Id, Inclusive: true);
    }

    private static ResumeStart StartAtStep(
        List<Node> nodes,
        Dictionary<string, Node> byId,
        List<Node> checkpoints,
        Dictionary<string, Node> latestInBranch,
        string resumeAt
    )
    {
        if (!byId.TryGetValue(resumeAt, out var point))
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.UnknownStep,
                    $"The chain declares no step '{resumeAt}'."
                )
            );

        if (point.Step.Kind is ChainStepKind.Seed or ChainStepKind.Extract or ChainStepKind.Resolve)
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.NotAStep,
                    $"'{resumeAt}' is not a step a run can start at. Resume at the step after it."
                )
            );

        // A routing step that asks its own question records the question as a Decide just
        // before it, and routes on what that asks: resuming at the routing step asks it again.
        if (
            point.Step.Kind is ChainStepKind.Switch or ChainStepKind.Gate or ChainStepKind.Scale
            && point.Order > 0
            && nodes[point.Order - 1] is { Step.Kind: ChainStepKind.Decide } question
            && string.Equals(question.Scope, point.Scope, StringComparison.Ordinal)
            && question.Step.Out == point.Step.In
        )
        {
            point = question;
            resumeAt = question.Id;
        }

        if (point.BranchPath is not null)
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.InsideABranch,
                    $"'{resumeAt}' is inside the Parallel branch '{point.BranchPath}'. Resume at "
                        + "the Parallel step: each branch resumes from its own checkpoint."
                )
            );

        return StartBeforePoint(nodes, byId, checkpoints, latestInBranch, point, resumeAt);
    }

    private static ResumeStart StartBeforePoint(
        List<Node> nodes,
        Dictionary<string, Node> byId,
        List<Node> checkpoints,
        Dictionary<string, Node> latestInBranch,
        Node point,
        string resumeAt
    )
    {
        var main = checkpoints
            .Where(n => n.BranchPath is null && n.Order < point.Order)
            .MaxBy(n => n.Order);

        var branchCheckpoints = BranchesThatHold(nodes, byId, latestInBranch, main, point);

        if (main is null && branchCheckpoints.Count == 0)
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.NoCheckpoint,
                    $"No checkpoint the run wrote comes before '{resumeAt}', so it can only run "
                        + "again from the top."
                )
            );

        // The point must be on the path to the checkpoint: a track the run entered after it
        // was entered by a routing step the resumed run would skip, and never asks.
        if (main is not null && !main.Scope.StartsWith(point.Scope, StringComparison.Ordinal))
            return ResumeStart.Refused(
                ResumeOutcome.Refused(
                    ResumeRefusals.OffThePath,
                    $"'{resumeAt}' is in a track of a routing step after the checkpoint "
                        + $"'{main.Id}', which a resumed run would not ask. Resume at that "
                        + "routing step."
                )
            );

        return new ResumeStart(Refusal: null, main, branchCheckpoints, resumeAt, Inclusive: false);
    }

    private static ResumeOutcome WalkFromStart(
        ChainRecorder chain,
        Type input,
        Type output,
        Func<Type, bool>? availableElsewhere,
        Func<Type, bool>? withheld,
        List<Node> nodes,
        ResumeStart start
    )
    {
        var main = start.Main;
        var branchCheckpoints = start.BranchCheckpoints;
        var (stateTypes, trackTypes) = RestoredTypes(nodes, start, withheld);

        // The walk: Memory as the resumed run starts, then every step from the point on.
        var memory = new System.Collections.Generic.HashSet<Type> { typeof(Unit) };
        Remember(memory, input, withInterfaces: true);

        if (main is not null)
        {
            memory.Add(stateTypes[main.Id]);
            memory.UnionWith(trackTypes[main.Id]);
        }

        var walk = NewResumeWalk(start, stateTypes, trackTypes);

        var faults = new List<ChainFault>();
        Replay(
            chain,
            memory,
            faults,
            output,
            availableElsewhere,
            checkConstructors: false,
            track: null,
            walk,
            new ChainNodeScope("")
        );

        if (faults.Count > 0)
            return ResumeOutcome.Refused(
                walk.ReadStale ? ResumeRefusals.StaleValue : ResumeRefusals.MissingInput,
                string.Join(' ', faults.Select(f => f.Reason))
            );

        return new ResumeOutcome(
            RefusalCode: null,
            Refusal: null,
            start.Target,
            start.Inclusive,
            main?.Id,
            branchCheckpoints.ToDictionary(b => b.Key, b => b.Value.Id, StringComparer.Ordinal),
            stateTypes,
            trackTypes
        );
    }

    /// <summary>
    /// What each checkpoint a resume restores holds: its state type, and the route of every
    /// routing step the run passed before it.
    /// </summary>
    private static (
        Dictionary<string, Type> StateTypes,
        Dictionary<string, IReadOnlyList<Type>> TrackTypes
    ) RestoredTypes(List<Node> nodes, ResumeStart start, Func<Type, bool>? withheld)
    {
        var restored = new List<Node>();
        if (start.Main is not null)
            restored.Add(start.Main);
        restored.AddRange(start.BranchCheckpoints.Values);

        var stateTypes = restored.ToDictionary(
            n => n.Id,
            n => n.Step.In.NotNull(),
            StringComparer.Ordinal
        );
        var trackTypes = restored.ToDictionary(
            n => n.Id,
            n => (IReadOnlyList<Type>)TracksBefore(nodes, n, withheld).ToList(),
            StringComparer.Ordinal
        );

        return (stateTypes, trackTypes);
    }

    private static ResumeWalk NewResumeWalk(
        ResumeStart start,
        Dictionary<string, Type> stateTypes,
        Dictionary<string, IReadOnlyList<Type>> trackTypes
    ) =>
        new(
            start.Target,
            start.Inclusive,
            start.Main is null ? [] : [stateTypes[start.Main.Id], .. trackTypes[start.Main.Id]],
            start.BranchCheckpoints.ToDictionary(
                b => b.Key,
                b =>
                    (
                        b.Value.Id,
                        (IReadOnlyList<Type>)[stateTypes[b.Value.Id], .. trackTypes[b.Value.Id]]
                    ),
                StringComparer.Ordinal
            )
        );

    /// <summary>
    /// Refuses a stored checkpoint that no longer matches the running code: the chain's hash, or
    /// its state type's fingerprint, differs from what was stored with it. Null when both match.
    /// </summary>
    /// <param name="nodeId">The checkpoint's node id.</param>
    /// <param name="storedChainHash">The chain hash stored with the checkpoint.</param>
    /// <param name="currentChainHash">The running chain's hash.</param>
    /// <param name="storedFingerprint">The state fingerprint stored with the checkpoint.</param>
    /// <param name="stateType">The state type the running chain declares at that node.</param>
    internal static ResumeOutcome? CheckStored(
        string nodeId,
        string storedChainHash,
        string currentChainHash,
        string storedFingerprint,
        Type stateType
    )
    {
        if (!string.Equals(storedChainHash, currentChainHash, StringComparison.Ordinal))
            return ResumeOutcome.Refused(
                ResumeRefusals.ChainChanged,
                $"The checkpoint '{nodeId}' was written by a different version of the chain, "
                    + "so its steps may not line up. Run it again from the top."
            );

        if (
            !string.Equals(
                storedFingerprint,
                CheckpointState.Fingerprint(stateType),
                StringComparison.Ordinal
            )
        )
            return ResumeOutcome.Refused(
                ResumeRefusals.StateChanged,
                $"The checkpoint '{nodeId}' stored '{stateType.FullName}' in a shape the type no "
                    + "longer has, so reading it back would quietly default members. Run it "
                    + "again from the top."
            );

        return null;
    }

    /// <summary>
    /// The branch checkpoints a resume can restore. A branch's checkpoint holds what the branch
    /// computed from Memory as it was when the run reached its <c>Parallel</c>, so it holds only
    /// while that Memory is what the resumed run has there too: when the run resumes at the
    /// <c>Parallel</c> itself, or after a main checkpoint with nothing between it and the
    /// <c>Parallel</c> that runs again but a value handed to the chain or extracted from Memory.
    /// Any other step between would run again and could produce a different value, which a
    /// restored branch would never read. A <c>Parallel</c> inside a branch has its enclosing
    /// <c>Parallel</c> between, so its branches never restore and run from their start.
    /// </summary>
    /// <param name="nodes">The chain's nodes, in order.</param>
    /// <param name="byId">The same nodes, by id.</param>
    /// <param name="written">The latest checkpoint each branch wrote, by branch path.</param>
    /// <param name="main">The checkpoint the main chain restores, or null.</param>
    /// <param name="point">The step an operator resumes at, or null for after the latest checkpoint.</param>
    private static Dictionary<string, Node> BranchesThatHold(
        List<Node> nodes,
        Dictionary<string, Node> byId,
        Dictionary<string, Node> written,
        Node? main,
        Node? point
    ) =>
        written
            .Where(b =>
                byId.TryGetValue(b.Key[..b.Key.LastIndexOf('/')], out var parallel)
                && (
                    point is not null
                        ? string.Equals(parallel.Id, point.Id, StringComparison.Ordinal)
                        : parallel.Order > (main?.Order ?? -1)
                            && nodes
                                .Skip((main?.Order ?? -1) + 1)
                                .Take(parallel.Order - (main?.Order ?? -1) - 1)
                                .All(n =>
                                    n.Step.Kind is ChainStepKind.Seed or ChainStepKind.Extract
                                )
                )
            )
            .ToDictionary(b => b.Key, b => b.Value, StringComparer.Ordinal);

    /// <summary>
    /// The ids of every step of the chain, its tracks' and its branches', in the order a run
    /// reaches them.
    /// </summary>
    internal static IReadOnlyList<string> NodeOrder(ChainRecorder chain) =>
        Walk(chain).Select(n => n.Id).ToList();

    /// <summary>One declared step as a resume sees it.</summary>
    private sealed record Node(
        string Id,
        ChainStep Step,
        string Scope,
        string? BranchPath,
        int Order
    );

    private static List<Node> Walk(ChainRecorder chain)
    {
        var nodes = new List<Node>();
        Visit(chain, new ChainNodeScope(""), branchPath: null);
        return nodes;

        void Visit(ChainRecorder steps, ChainNodeScope scope, string? branchPath)
        {
            for (var i = 0; i < steps.Steps.Count; i++)
            {
                var step = steps.Steps[i];
                var id = scope.Next(ChainNodeScope.KeyOf(step));
                nodes.Add(new Node(id, step, scope.Prefix, branchPath, nodes.Count));

                foreach (var track in steps.TracksAt(i))
                    Visit(
                        track.Steps,
                        scope.Track(id, track.Name),
                        step.Kind == ChainStepKind.Parallel ? $"{id}/{track.Name}" : branchPath
                    );
            }
        }
    }

    /// <summary>
    /// The <see cref="TrackTaken{TKey}"/> types a checkpoint can hold: one for each routing step a
    /// run passed before reaching it, which is every routing step before it in a chain or track
    /// that encloses it, but one whose route the store withholds.
    /// </summary>
    private static IEnumerable<Type> TracksBefore(
        List<Node> nodes,
        Node checkpoint,
        Func<Type, bool>? withheld
    ) =>
        nodes
            .Where(n =>
                n.Order < checkpoint.Order
                && n.Step.Kind is ChainStepKind.Switch or ChainStepKind.Gate or ChainStepKind.Scale
                && checkpoint.Scope.StartsWith(n.Scope, StringComparison.Ordinal)
                && n.Step.Out is { IsGenericType: true } taken
                && taken.GetGenericTypeDefinition() == typeof(TrackTaken<>)
                && withheld?.Invoke(taken.GetGenericArguments()[0]) != true
            )
            .Select(n => n.Step.Out.NotNull())
            .Distinct();
}
