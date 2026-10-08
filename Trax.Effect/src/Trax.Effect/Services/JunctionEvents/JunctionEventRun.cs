using System.Collections.Concurrent;
using Trax.Core.Monad;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// One run's junction events: the run they are about, and the next position in its timeline.
/// </summary>
/// <remarks>
/// It lives on the run's own async flow (<see cref="Current"/>), set by <c>ServiceTrain.Run</c>
/// for a host that called <c>AddJunctionEvents</c> and cleared when its junctions finish, as
/// <see cref="DecisionRun"/> is. A train run inside a junction sets its own for its own flow, so
/// each run's steps are numbered and reported under its own row.
/// </remarks>
internal sealed class JunctionEventRun
{
    private static readonly AsyncLocal<JunctionEventRun?> CurrentRun = new();

    private readonly JunctionEventPublisher _publisher;
    private int _position = -1;

    public JunctionEventRun(
        JunctionEventPublisher publisher,
        Metadata metadata,
        Type train,
        IServiceProvider services,
        int? attempt = null
    )
    {
        Attempt = attempt;
        _publisher = publisher;
        Metadata = metadata;
        ExternalId = metadata.ExternalId;
        DecisionTrain = DecisionRun.NameOf(train);
        Services = services;
    }

    /// <summary>The run on this async flow, or null when junction events are off or no run is going.</summary>
    public static JunctionEventRun? Current
    {
        get => CurrentRun.Value;
        set => CurrentRun.Value = value;
    }

    /// <summary>The run on this flow when it is the run <paramref name="metadata"/> records.</summary>
    public static JunctionEventRun? For(Metadata? metadata) =>
        Current is { } run && metadata is not null && ReferenceEquals(run.Metadata, metadata)
            ? run
            : null;

    /// <summary>
    /// The run on this flow when Trax.Core reported a decision for it: the same train, under the
    /// run's external id. A decision of another train run on the same flow is not this run's.
    /// </summary>
    public static JunctionEventRun? ForDecision(string train, string runId) =>
        Current is { } run && run.DecisionTrain == train && run.ExternalId == runId ? run : null;

    /// <summary>The run's row.</summary>
    public Metadata Metadata { get; }

    /// <summary>The run's external id when it began.</summary>
    public string ExternalId { get; }

    /// <summary>The train as Trax.Core names it in its decisions.</summary>
    public string DecisionTrain { get; }

    /// <summary>
    /// Which attempt of its manifest the run is, worked out once when it began, or null for a run
    /// with no manifest or when it could not be worked out.
    /// </summary>
    public int? Attempt { get; }

    /// <summary>The run's scope, which local junction event handlers are resolved from.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// The track and withholding of each branch that has taken a track, keyed by branch path
    /// (<see cref="BranchPaths"/>). The run's own chain is the branch <see cref="BranchPaths.Run"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lane> _lanes = new();

    /// <summary>What one branch's routing steps have done to its later steps.</summary>
    private sealed class Lane
    {
        private int _trackPosition = -1;
        private int _withholds;

        public int? TrackPosition => Volatile.Read(ref _trackPosition) is >= 0 and var p ? p : null;

        public bool WithholdsNames => Volatile.Read(ref _withholds) != 0;

        public void Routed(int position, bool withheld)
        {
            Volatile.Write(ref _trackPosition, position);
            if (withheld)
                Volatile.Write(ref _withholds, 1);
        }
    }

    /// <summary>
    /// The position of the latest routing step the step on this flow follows, or null before any.
    /// Every junction after a routing step is counted as on its track, because Trax.Core does not
    /// report where tracks rejoin.
    /// </summary>
    /// <remarks>
    /// Inside a <c>Parallel</c> branch it is the branch's own latest routing step, or before the
    /// branch has taken one, the latest of the chain it was forked from. A sibling's routing steps
    /// never count: the sibling's steps are not on this branch's path. After the join, the run is
    /// on the track it was on before the fork, because the branches' tracks end at the join.
    /// </remarks>
    public int? TrackPosition => TrackPositionIn(BranchPaths.Current);

    /// <summary>
    /// True once the step on this flow follows a track whose answer is withheld. From then on the
    /// names of its junctions, questions and routing steps are withheld too, with the questions'
    /// keys and answers, since they would give the track away. It is never cleared, for the
    /// same reason <see cref="TrackPosition"/> never ends.
    /// </summary>
    /// <remarks>
    /// Inside a <c>Parallel</c> branch, a withheld track the branch took withholds the branch's
    /// later steps, and one taken before the fork withholds every branch. A sibling's withheld track
    /// does not withhold this branch's steps: they run beside that track, not on it, so their names
    /// give nothing of it away. Once the branches join, a withheld track taken in any of them
    /// withholds everything after the join, later branches included, because the steps after it
    /// follow every branch, the withheld one too.
    /// </remarks>
    public bool WithholdsNames => WithholdsNamesIn(BranchPaths.Current);

    private int? TrackPositionIn(string path)
    {
        string? nearest = null;
        int? position = null;

        foreach (var (lanePath, lane) in _lanes)
            if (
                BranchPaths.Encloses(lanePath, path)
                && lane.TrackPosition is { } taken
                && (nearest is null || lanePath.Length > nearest.Length)
            )
                (nearest, position) = (lanePath, taken);

        return position;
    }

    private bool WithholdsNamesIn(string path) =>
        _lanes.Any(l => l.Value.WithholdsNames && !BranchPaths.Alongside(l.Key, path));

    /// <summary>
    /// Records that the branch on this flow took the track a routing step at
    /// <paramref name="position"/> chose.
    /// </summary>
    public void Routed(int position, bool withheld) =>
        _lanes.GetOrAdd(BranchPaths.Current, _ => new Lane()).Routed(position, withheld);

    /// <summary>
    /// A step as the run's tracks so far require it to be published and stored: a junction, a
    /// question or a routing step. While <see cref="WithholdsNames"/> is set, its name is withheld,
    /// and so are a question's or a routing step's key, answer, confidence and decider, because
    /// what a track asks and how it routes would give the track away as much as its junctions'
    /// names. The node id and branch path are withheld with them, since they name the track and
    /// the step.
    /// </summary>
    /// <remarks>
    /// Called on the step's own flow, while Trax.Core holds the step's node in
    /// <see cref="ChainGraph.CurrentNodeId"/> and its branch in
    /// <see cref="ChainGraph.CurrentBranchPath"/>: a junction's start and end from inside its
    /// <c>RailwayJunction</c>, a question's or routing step's from the decision observer Trax.Core
    /// tells before it moves on.
    /// </remarks>
    public JunctionEventPayload OnTrack(JunctionEventPayload step)
    {
        var path = BranchPaths.Current;
        var trackPosition = TrackPositionIn(path);

        return WithholdsNamesIn(path)
            ? step with
            {
                Name = JunctionEventPayload.WithheldName,
                NameWithheld = true,
                QuestionKey = null,
                Answer = null,
                Confidence = null,
                Decider = null,
                AnswerWithheld = step.AnswerWithheld || step.Kind != JunctionRunKind.Junction,
                TrackPosition = trackPosition,
                NodeId = null,
                BranchPath = null,
            }
            : step with
            {
                TrackPosition = trackPosition,
                NodeId = ChainGraph.CurrentNodeId,
                BranchPath = ChainGraph.CurrentBranchPath,
            };
    }

    /// <summary>The next position in the run's timeline.</summary>
    public int NextPosition() => Interlocked.Increment(ref _position);

    /// <summary>Publishes one step. Never throws.</summary>
    public Task Publish(string eventType, JunctionEventPayload step) =>
        _publisher.Publish(this, eventType, step);
}
