using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum TopicMapState
{
    /// <summary>The wizard's first step: which fields of the corpus to map.</summary>
    ChoosingFields,

    /// <summary>The second step: which years.</summary>
    ChoosingRange,

    /// <summary>The map is being built: <see cref="IBuildTopicMapTrain"/> runs.</summary>
    Building,

    /// <summary>The build finished; the context points at the pairs it wrote.</summary>
    Built,

    /// <summary>The build failed, or its run was reaped after its host died.</summary>
    BuildFailed,

    /// <summary>
    /// The build's run was cancelled while the draft stayed in Building: it timed out. An operator cannot
    /// cancel it, because a user's draft is read-only to operators.
    /// </summary>
    BuildCancelled,

    /// <summary>
    /// The built map was not reviewed within <see cref="TopicMapMachine.ReviewWindow"/>: the server moved it here by
    /// the review timer. It can be rebuilt, or its range edited.
    /// </summary>
    Expired,
}

public enum TopicMapTrigger
{
    /// <summary>Choose the fields to map and go to the range.</summary>
    ChooseFields,

    /// <summary>Back from the range to the fields.</summary>
    Back,

    /// <summary>Choose the years and build the map.</summary>
    Build,

    /// <summary>Stop the build that is running and go back to the range.</summary>
    CancelBuild,

    /// <summary>Build again with the same choices: a new run.</summary>
    Rebuild,

    /// <summary>Change the choices after a build.</summary>
    Edit,

    /// <summary>
    /// Start a new map on this draft: back to the first step with nothing chosen. From Building it cancels the run,
    /// and from a finished build it leaves the build's state, so its review timer is cancelled.
    /// </summary>
    StartOver,
}

/// <summary>
/// The wizard's draft, one shape in every state: what the user chose, and once a build finished, a pointer
/// to what it wrote and a summary of it. Never the pairs themselves: those are in
/// <c>topic_map.topic_pairs</c> under <see cref="MapId"/>.
/// </summary>
public sealed record TopicMapContext
{
    public List<string>? Fields { get; init; }

    public int? FromYear { get; init; }

    public int? ToYear { get; init; }

    /// <summary>The run id the build wrote its pairs under.</summary>
    public string? MapId { get; init; }

    public int? Papers { get; init; }

    public int? TopicPairs { get; init; }

    public string? CoCitationTrack { get; init; }
}

/// <summary>The input of <see cref="TopicMapTrigger.ChooseFields"/>.</summary>
public sealed record FieldsChoice
{
    [MinLength(1)]
    public List<string> Fields { get; init; } = [];
}

/// <summary>What a user starts a topic map with: nothing, the wizard starts empty.</summary>
public sealed record TopicMapStart;

/// <summary>The input of <see cref="TopicMapTrigger.Build"/>.</summary>
public sealed record RangeChoice
{
    public int FromYear { get; init; }

    public int ToYear { get; init; }
}

/// <summary>
/// "Build my topic map": a wizard each user drives from the page, choose the fields, choose the years,
/// build. <c>Building</c> runs <see cref="IBuildTopicMapTrain"/>, and only that run's outcome moves the
/// draft on.
/// <code>
/// ChoosingFields --ChooseFields--> ChoosingRange --Build--> Building --done-----> Built
///       ▲             ◀──Back──         ▲ ◀──CancelBuild──  │ │      --failed---> BuildFailed
///       │                               └──────Edit─────────┼─┼─────────────────  Built, BuildFailed,
///       │                                                   │ └────cancelled----> BuildCancelled
///                                       Building ◀──Rebuild── Built, BuildFailed, BuildCancelled
///   ChoosingFields ◀──StartOver── Building, Built, BuildFailed, BuildCancelled, Expired (nothing chosen)
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// User-owned, so the client's twin drives it and the draft is the user's own: close the tab and
/// <c>machines { topicMap { get } }</c> returns the same place. The host exposes it, so a client creates a
/// draft with its typed <c>start</c>, which mints its id on the server, and moves it only with the machine's
/// events, each its own mutation. Because a client would like to skip the work, no client writes a snapshot
/// of it, and no client field fires <c>Building.done</c>: only the run's outcome does.
/// </para>
/// <para>
/// No outcome target invokes a train, so every move out of a finished build is a user's event:
/// <c>Rebuild</c> enters <c>Building</c> again, which queues a new run.
/// </para>
/// <para>
/// <c>StartOver</c> is how the page starts a new map once a build began. The server refuses to discard a draft in
/// <c>Building</c> or in a state a build's outcome reached, since it is the record of that build, so the page resets
/// the draft it holds instead of leaving it behind: an abandoned draft would count against the user's draft cap until
/// it expired, and a Built one's review timer would keep it alive past that.
/// </para>
/// <para>
/// Every event declares who may send it. <c>Build</c> and <c>Rebuild</c> enter <c>Building</c>, which runs
/// <see cref="IBuildTopicMapTrain"/>, so both require <see cref="RecoveryRoles.User"/>, the role the train
/// asks for: the host compares each event entering the state with the train at startup. Any authenticated user
/// may move through the rest of the wizard, and only a draft's owner reads it.
/// </para>
/// </remarks>
[TraxMachine]
public sealed class TopicMapMachine : Machine<TopicMapState, TopicMapTrigger>
{
    public const string MachineId = "topic-map";

    /// <summary>How long a built map is held for review before the server moves it to <c>Expired</c>.</summary>
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(7);

    // Declared before anything reads it: static fields initialize in order.
    private static readonly Reduction KeepTheMap = SetAll(
        Set((TopicMapContext c) => c.MapId).FromInput((TopicMap o) => o.RunId),
        Set((TopicMapContext c) => c.Papers).FromInput((TopicMap o) => o.Papers),
        Set((TopicMapContext c) => c.TopicPairs).FromInput((TopicMap o) => o.TopicPairs),
        Set((TopicMapContext c) => c.CoCitationTrack).FromInput((TopicMap o) => o.CoCitationTrack)
    );

    private static readonly Rule HasFields = Field((TopicMapContext c) => c.Fields).CountAtLeast(1);

    private static readonly Rule HasRange = All(
        HasFields,
        Field((TopicMapContext c) => c.FromYear).Present(),
        Field((TopicMapContext c) => c.ToYear).Present()
    );

    protected override void Configure(IMachineBuilder<TopicMapState, TopicMapTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(TopicMapState.ChoosingFields, () => new JsonObject())
            .Start<TopicMapStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(TopicMapState.ChoosingFields)
            .Context<TopicMapContext>()
            .On(TopicMapTrigger.ChooseFields)
            .AnyAuthenticated()
            .WithInput<FieldsChoice>()
            .When(Input((FieldsChoice i) => i.Fields).CountAtLeast(1))
            .Because("Choose at least one field.")
            .Reduce(Set((TopicMapContext c) => c.Fields).FromInput((FieldsChoice i) => i.Fields))
            .To(TopicMapState.ChoosingRange);

        m.In(TopicMapState.ChoosingRange)
            .Context<TopicMapContext>()
            .Requires(HasFields)
            .On(TopicMapTrigger.Back)
            .AnyAuthenticated()
            .To(TopicMapState.ChoosingFields)
            .On(TopicMapTrigger.Build)
            .Requires(RecoveryRoles.User)
            .WithInput<RangeChoice>()
            .When(
                All(
                    Input((RangeChoice i) => i.FromYear).OfType(JsonFieldType.Number),
                    Input((RangeChoice i) => i.ToYear).OfType(JsonFieldType.Number)
                )
            )
            .Because("Choose the first and the last year.")
            .Reduce(
                SetAll(
                    Set((TopicMapContext c) => c.FromYear).FromInput((RangeChoice i) => i.FromYear),
                    Set((TopicMapContext c) => c.ToYear).FromInput((RangeChoice i) => i.ToYear)
                )
            )
            .To(TopicMapState.Building);

        m.In(TopicMapState.Building)
            .Context<TopicMapContext>()
            .Requires(HasRange)
            .Invokes<IBuildTopicMapTrain, TopicMapInput, TopicMap>(BuildInput)
            .OnDone(TopicMapState.Built, reduce: KeepTheMap)
            .OnFailed(TopicMapState.BuildFailed)
            .OnCancelled(TopicMapState.BuildCancelled)
            // Leaving Building cancels its run.
            .On(TopicMapTrigger.CancelBuild)
            .AnyAuthenticated()
            .To(TopicMapState.ChoosingRange)
            .On(TopicMapTrigger.StartOver)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(TopicMapState.ChoosingFields);

        m.In(TopicMapState.Built)
            .Context<TopicMapContext>()
            .Requires(All(HasRange, Field((TopicMapContext c) => c.MapId).NonEmpty()))
            // A built map is reviewed within a window; once it passes, the server moves the map on by itself, and the
            // page hears it by push.
            .After(ReviewWindow)
            .GoTo(TopicMapState.Expired)
            .On(TopicMapTrigger.Rebuild)
            .Requires(RecoveryRoles.User)
            .To(TopicMapState.Building)
            .On(TopicMapTrigger.Edit)
            .AnyAuthenticated()
            .To(TopicMapState.ChoosingRange)
            .On(TopicMapTrigger.StartOver)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(TopicMapState.ChoosingFields);

        foreach (
            var ended in new[]
            {
                TopicMapState.BuildFailed,
                TopicMapState.BuildCancelled,
                TopicMapState.Expired,
            }
        )
            m.In(ended)
                .Context<TopicMapContext>()
                .Requires(HasRange)
                .On(TopicMapTrigger.Rebuild)
                .Requires(RecoveryRoles.User)
                .To(TopicMapState.Building)
                .On(TopicMapTrigger.Edit)
                .AnyAuthenticated()
                .To(TopicMapState.ChoosingRange)
                .On(TopicMapTrigger.StartOver)
                .AnyAuthenticated()
                .Reduce(Reset())
                .To(TopicMapState.ChoosingFields);

        m.Differential(d =>
            d.Sample(TopicMapTrigger.ChooseFields, new FieldsChoice { Fields = ["Hydrology"] })
                .Sample(TopicMapTrigger.Build, new RangeChoice { FromYear = 2016, ToYear = 2025 })
                // A whole number no int holds: both engines refuse it as invalid-input.
                .Sample(
                    TopicMapTrigger.Build,
                    new JsonObject { ["fromYear"] = 1e10, ["toYear"] = 2025 }
                )
                .EmptySample(TopicMapTrigger.ChooseFields)
                .OutcomeSample(
                    TopicMapState.Building,
                    new TopicMap("map-sample", 28, 32, "Trusted", [], [])
                )
        );
    }

    // The run's input, built on the server from the context Building was entered with. Each entry gets a
    // run id of its own, which the pairs it writes are keyed by, so a rebuild never overwrites an earlier
    // map and no client chooses whose pairs a run replaces.
    private static TopicMapInput BuildInput(JsonObject ctx) =>
        new()
        {
            RunId = $"map-{Guid.NewGuid():N}",
            Fields = ctx["fields"]!.AsArray().Select(f => f!.GetValue<string>()).ToList(),
            FromYear = ctx["fromYear"]!.GetValue<int>(),
            ToYear = ctx["toYear"]!.GetValue<int>(),
        };

    // One reduction that sets several fields. Rules.Set sets one; the Set reduction holds a list of steps.
    private static Reduction SetAll(params Reduction[] sets) =>
        new Reduction.Set(sets.SelectMany(s => ((Reduction.Set)s).Steps).ToList());
}
