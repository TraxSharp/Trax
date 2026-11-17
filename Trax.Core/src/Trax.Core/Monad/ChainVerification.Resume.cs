using Trax.Core.Decisions;
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
            .GroupBy(n => n.BranchPath!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(n => n.Order)!, StringComparer.Ordinal);

        Node? main;
        string? target;
        bool inclusive;
        Dictionary<string, Node> branchCheckpoints;

        if (resumeAt is null)
        {
            main = checkpoints.Where(n => n.BranchPath is null).MaxBy(n => n.Order);
            branchCheckpoints = BranchesThatHold(nodes, byId, latestInBranch, main, point: null);

            if (main is null && branchCheckpoints.Count == 0)
                return ResumeOutcome.Refused(
                    ResumeRefusals.NoCheckpoint,
                    "The run wrote no checkpoint, so it can only run again from the top."
                );

            (target, inclusive) = (main?.Id, true);
        }
        else
        {
            if (!byId.TryGetValue(resumeAt, out var point))
                return ResumeOutcome.Refused(
                    ResumeRefusals.UnknownStep,
                    $"The chain declares no step '{resumeAt}'."
                );

            if (
                point.Step.Kind
                is ChainStepKind.Seed
                    or ChainStepKind.Extract
                    or ChainStepKind.Resolve
            )
                return ResumeOutcome.Refused(
                    ResumeRefusals.NotAStep,
                    $"'{resumeAt}' is not a step a run can start at. Resume at the step after it."
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
                return ResumeOutcome.Refused(
                    ResumeRefusals.InsideABranch,
                    $"'{resumeAt}' is inside the Parallel branch '{point.BranchPath}'. Resume at "
                        + "the Parallel step: each branch resumes from its own checkpoint."
                );

            main = checkpoints
                .Where(n => n.BranchPath is null && n.Order < point.Order)
                .MaxBy(n => n.Order);

            branchCheckpoints = BranchesThatHold(nodes, byId, latestInBranch, main, point);

            if (main is null && branchCheckpoints.Count == 0)
                return ResumeOutcome.Refused(
                    ResumeRefusals.NoCheckpoint,
                    $"No checkpoint the run wrote comes before '{resumeAt}', so it can only run "
                        + "again from the top."
                );

            // The point must be on the path to the checkpoint: a track the run entered after it
            // was entered by a routing step the resumed run would skip, and never asks.
            if (main is not null && !main.Scope.StartsWith(point.Scope, StringComparison.Ordinal))
                return ResumeOutcome.Refused(
                    ResumeRefusals.OffThePath,
                    $"'{resumeAt}' is in a track of a routing step after the checkpoint "
                        + $"'{main.Id}', which a resumed run would not ask. Resume at that "
                        + "routing step."
                );

            (target, inclusive) = (resumeAt, false);
        }

        var restored = new List<Node>();
        if (main is not null)
            restored.Add(main);
        restored.AddRange(branchCheckpoints.Values);

        var stateTypes = restored.ToDictionary(n => n.Id, n => n.Step.In!, StringComparer.Ordinal);
        var trackTypes = restored.ToDictionary(
            n => n.Id,
            n => (IReadOnlyList<Type>)TracksBefore(nodes, n, withheld).ToList(),
            StringComparer.Ordinal
        );

        // The walk: Memory as the resumed run starts, then every step from the point on.
        var memory = new System.Collections.Generic.HashSet<Type> { typeof(Unit) };
        Remember(memory, input, withInterfaces: true);

        if (main is not null)
        {
            memory.Add(stateTypes[main.Id]);
            memory.UnionWith(trackTypes[main.Id]);
        }

        var walk = new ResumeWalk(
            target,
            inclusive,
            main is null ? [] : [stateTypes[main.Id], .. trackTypes[main.Id]],
            branchCheckpoints.ToDictionary(
                b => b.Key,
                b =>
                    (
                        b.Value.Id,
                        (IReadOnlyList<Type>)[stateTypes[b.Value.Id], .. trackTypes[b.Value.Id]]
                    ),
                StringComparer.Ordinal
            )
        );

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
            target,
            inclusive,
            main?.Id,
            branchCheckpoints.ToDictionary(b => b.Key, b => b.Value.Id, StringComparer.Ordinal),
            stateTypes,
            trackTypes
        );
    }

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
            .Select(n => n.Step.Out!)
            .Distinct();
}
