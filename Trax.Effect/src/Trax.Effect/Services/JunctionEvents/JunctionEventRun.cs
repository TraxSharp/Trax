using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.Decisions;
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
    /// The position of the latest routing step the run took, or null before any. Every junction
    /// after it is counted as on its track, because Trax.Core does not report where tracks rejoin.
    /// </summary>
    public int? TrackPosition { get; private set; }

    /// <summary>
    /// True once the run has taken a track whose answer is withheld. From then on the names of its
    /// junctions, questions and routing steps are withheld too, with the questions' keys and
    /// answers, since they would give the track away. It is never cleared, for the
    /// same reason <see cref="TrackPosition"/> never ends.
    /// </summary>
    public bool WithholdsNames { get; private set; }

    /// <summary>Records that the run took the track a routing step at <paramref name="position"/> chose.</summary>
    public void Routed(int position, bool withheld)
    {
        TrackPosition = position;
        if (withheld)
            WithholdsNames = true;
    }

    /// <summary>
    /// A step as the run's tracks so far require it to be published and stored: a junction, a
    /// question or a routing step. While <see cref="WithholdsNames"/> is set, its name is withheld,
    /// and so are a question's or a routing step's key, answer, confidence and decider, because
    /// what a track asks and how it routes would give the track away as much as its junctions'
    /// names.
    /// </summary>
    public JunctionEventPayload OnTrack(JunctionEventPayload step) =>
        WithholdsNames
            ? step with
            {
                Name = JunctionEventPayload.WithheldName,
                NameWithheld = true,
                QuestionKey = null,
                Answer = null,
                Confidence = null,
                Decider = null,
                AnswerWithheld = step.AnswerWithheld || step.Kind != JunctionRunKind.Junction,
                TrackPosition = TrackPosition,
            }
            : step with
            {
                TrackPosition = TrackPosition,
            };

    /// <summary>The next position in the run's timeline.</summary>
    public int NextPosition() => Interlocked.Increment(ref _position);

    /// <summary>Publishes one step. Never throws.</summary>
    public Task Publish(string eventType, JunctionEventPayload step) =>
        _publisher.Publish(this, eventType, step);
}
