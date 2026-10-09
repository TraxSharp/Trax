using Trax.Core.Decisions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>
/// Whether a run can resume, and from where: the plan when it can, the reason when it cannot.
/// </summary>
/// <param name="RefusalCode">Why it cannot, as a stable code, or null when it can. See <see cref="ResumeRefusals"/>.</param>
/// <param name="Refusal">Why it cannot, phrased for an operator, or null when it can.</param>
/// <param name="Target">The node the resumed run skips to, or null to run the main chain from the top.</param>
/// <param name="Inclusive">True when the target is a checkpoint the run restores rather than runs.</param>
/// <param name="MainCheckpoint">The checkpoint the main chain restores, or null.</param>
/// <param name="BranchCheckpoints">The checkpoint each branch restores, by branch path.</param>
/// <param name="StateTypes">Each restored checkpoint's declared state type, by node id.</param>
/// <param name="TrackTypes">
/// The <see cref="TrackTaken{TKey}"/> types each restored checkpoint can hold, by node id: one
/// for every routing step before it on its path.
/// </param>
internal sealed record ResumeOutcome(
    string? RefusalCode,
    string? Refusal,
    string? Target,
    bool Inclusive,
    string? MainCheckpoint,
    IReadOnlyDictionary<string, string> BranchCheckpoints,
    IReadOnlyDictionary<string, Type> StateTypes,
    IReadOnlyDictionary<string, IReadOnlyList<Type>> TrackTypes
)
{
    /// <summary>True when the run can resume as planned.</summary>
    public bool CanResume => RefusalCode is null;

    internal static ResumeOutcome Refused(string code, string reason) =>
        new(
            code,
            reason,
            null,
            false,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, Type>(),
            new Dictionary<string, IReadOnlyList<Type>>()
        );
}

/// <summary>The codes a refused resume carries, each with its own reason.</summary>
internal static class ResumeRefusals
{
    /// <summary>The run wrote no checkpoint that covers the point.</summary>
    public const string NoCheckpoint = "no-checkpoint";

    /// <summary>The chain declares no step with that id.</summary>
    public const string UnknownStep = "unknown-step";

    /// <summary>The id names a step a run cannot start at: an extraction, a seed or the end.</summary>
    public const string NotAStep = "not-a-step";

    /// <summary>The point is inside a <c>Parallel</c> branch; a branch resumes from its own checkpoint.</summary>
    public const string InsideABranch = "inside-a-branch";

    /// <summary>The point is in a track of a routing step after the checkpoint, which the run would not ask.</summary>
    public const string OffThePath = "off-the-path";

    /// <summary>A step from the point on needs a type nothing restores or produces.</summary>
    public const string MissingInput = "missing-input";

    /// <summary>
    /// A step from the point on reads a type a skipped step overwrote, which the checkpoint does
    /// not hold: a resumed run would read an older value.
    /// </summary>
    public const string StaleValue = "stale-value";

    /// <summary>The chain changed since the checkpoint was written.</summary>
    public const string ChainChanged = "chain-changed";

    /// <summary>The checkpoint's state type changed shape since it was written.</summary>
    public const string StateChanged = "state-changed";
}

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
        var byId = nodes.ToDictionary(n => n.Id);

        var checkpoints = nodes
            .Where(n => n.Step.Kind == ChainStepKind.Checkpoint && written.Contains(n.Id))
            .ToList();

        var latestInBranch = checkpoints
            .Where(n => n.BranchPath is not null)
            .GroupBy(n => n.BranchPath!)
            .ToDictionary(g => g.Key, g => g.MaxBy(n => n.Order)!);

        Node? main;
        string? target;
        bool inclusive;
        Dictionary<string, Node> branchCheckpoints;

        if (resumeAt is null)
        {
            main = checkpoints.Where(n => n.BranchPath is null).MaxBy(n => n.Order);
            branchCheckpoints = BranchesThatHold(nodes, byId, latestInBranch, main, null);

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
                && question.Scope == point.Scope
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

        var stateTypes = restored.ToDictionary(n => n.Id, n => n.Step.In!);
        var trackTypes = restored.ToDictionary(
            n => n.Id,
            n => (IReadOnlyList<Type>)TracksBefore(nodes, n, withheld).ToList()
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
                    )
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
                string.Join(" ", faults.Select(f => f.Reason))
            );

        return new ResumeOutcome(
            null,
            null,
            target,
            inclusive,
            main?.Id,
            branchCheckpoints.ToDictionary(b => b.Key, b => b.Value.Id),
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
                        ? parallel.Id == point.Id
                        : parallel.Order > (main?.Order ?? -1)
                            && nodes
                                .Skip((main?.Order ?? -1) + 1)
                                .Take(parallel.Order - (main?.Order ?? -1) - 1)
                                .All(n =>
                                    n.Step.Kind is ChainStepKind.Seed or ChainStepKind.Extract
                                )
                )
            )
            .ToDictionary(b => b.Key, b => b.Value);

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
        Visit(chain, new ChainNodeScope(""), null);
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

/// <summary>
/// Where a resume walk is: the point it skips to, whether it has reached it, and what each branch
/// restores.
/// </summary>
internal sealed class ResumeWalk(
    string? target,
    bool inclusive,
    IReadOnlyCollection<Type> restores,
    IReadOnlyDictionary<string, (string Checkpoint, IReadOnlyList<Type> Restores)> branches
)
{
    /// <summary>What the checkpoint this walk starts from restores, as it was when it was taken.</summary>
    public System.Collections.Generic.HashSet<Type> Restores { get; } = [.. restores];

    /// <summary>The types a skipped step wrote that nothing restores.</summary>
    public System.Collections.Generic.HashSet<Type> Stale { get; } = [];

    /// <summary>True once a step from the point on read a stale type, here or in a branch.</summary>
    public bool ReadStale { get; private set; }

    /// <summary>The walk a branch's walk started from, which learns that a branch read a stale type.</summary>
    private ResumeWalk? _parent;

    /// <summary>Whether <paramref name="type"/> is stale, noting that a step read it.</summary>
    public bool ReadsStale(Type type)
    {
        if (!Stale.Contains(type))
            return false;

        for (var walk = this; walk is not null; walk = walk._parent)
            walk.ReadStale = true;

        return true;
    }

    public string Target { get; } = target ?? "";

    public bool Inclusive { get; } = inclusive;

    public bool Skipping { get; set; } = target is not null;

    /// <summary>
    /// The walk of the branch at <paramref name="path"/>: from its own checkpoint when it has one,
    /// whose state and routes go into <paramref name="memory"/>, else from its start. A type a
    /// skipped step before the <c>Parallel</c> overwrote is stale in the branch too, unless the
    /// branch's own checkpoint holds it.
    /// </summary>
    public ResumeWalk ForBranch(string path, System.Collections.Generic.HashSet<Type> memory)
    {
        ResumeWalk walk;

        if (branches.TryGetValue(path, out var restored))
        {
            memory.UnionWith(restored.Restores);
            walk = new ResumeWalk(restored.Checkpoint, true, restored.Restores, branches);
        }
        else
            walk = new ResumeWalk(null, false, [], branches);

        walk._parent = this;
        walk.Stale.UnionWith(Stale.Where(t => !walk.Restores.Contains(t)));
        return walk;
    }
}
