using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
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

    /// <summary>The build's run was cancelled by an operator or timed out.</summary>
    BuildCancelled,
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
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// User-owned, so the client's twin drives it and the draft is the user's own: close the tab and
/// <c>loadSnapshot</c> returns the same place. Because a client would like to skip the work, every
/// state an outcome reaches is reserved: an autosave cannot put a draft in <c>Building</c>,
/// <c>Built</c>, <c>BuildFailed</c> or <c>BuildCancelled</c>, and <c>advanceSnapshot</c> refuses
/// <c>Building.done</c>.
/// </para>
/// <para>
/// No outcome target invokes a train, so every move out of a finished build is a user's event:
/// <c>Rebuild</c> enters <c>Building</c> again, which queues a new run.
/// </para>
/// </remarks>
public sealed class TopicMapMachine : Machine<TopicMapState, TopicMapTrigger>
{
    public const string MachineId = "topic-map";

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
        m.Id(MachineId).Version(1).StartsAt(TopicMapState.ChoosingFields, () => new JsonObject());

        m.In(TopicMapState.ChoosingFields)
            .Context<TopicMapContext>()
            .On(TopicMapTrigger.ChooseFields)
            .WithInput<FieldsChoice>()
            .When(Input((FieldsChoice i) => i.Fields).CountAtLeast(1))
            .Because("Choose at least one field.")
            .Reduce(Set((TopicMapContext c) => c.Fields).FromInput((FieldsChoice i) => i.Fields))
            .To(TopicMapState.ChoosingRange);

        m.In(TopicMapState.ChoosingRange)
            .Context<TopicMapContext>()
            .Requires(HasFields)
            .On(TopicMapTrigger.Back)
            .To(TopicMapState.ChoosingFields)
            .On(TopicMapTrigger.Build)
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
            .To(TopicMapState.ChoosingRange);

        m.In(TopicMapState.Built)
            .Context<TopicMapContext>()
            .Requires(All(HasRange, Field((TopicMapContext c) => c.MapId).NonEmpty()))
            .On(TopicMapTrigger.Rebuild)
            .To(TopicMapState.Building)
            .On(TopicMapTrigger.Edit)
            .To(TopicMapState.ChoosingRange);

        foreach (var ended in new[] { TopicMapState.BuildFailed, TopicMapState.BuildCancelled })
            m.In(ended)
                .Context<TopicMapContext>()
                .Requires(HasRange)
                .On(TopicMapTrigger.Rebuild)
                .To(TopicMapState.Building)
                .On(TopicMapTrigger.Edit)
                .To(TopicMapState.ChoosingRange);

        m.Differential(d =>
            d.Sample(TopicMapTrigger.ChooseFields, new FieldsChoice { Fields = ["Hydrology"] })
                .Sample(TopicMapTrigger.Build, new RangeChoice { FromYear = 2016, ToYear = 2025 })
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
