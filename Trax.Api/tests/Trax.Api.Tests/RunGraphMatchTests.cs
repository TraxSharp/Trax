using AwesomeAssertions;
using Trax.Api.DTOs;
using Trax.Api.Services.Runs;
using Trax.Core.Monad;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;

namespace Trax.Api.Tests;

/// <summary>
/// <c>RunGraphs.Match</c>, the overlay <c>operations.runGraph</c> and the dashboard's run
/// graph share: a step lands on the node its node id names and nowhere else, a node's state is the
/// worst of its steps, the track taken is the route's answer or the one track that ran, the tracks
/// not taken are skipped, and a step that names no node is kept as unmatched.
/// </summary>
[TestFixture]
public class RunGraphMatchTests
{
    private const string Decide = "Decide<ChoiceDecision<Lane>>#0";
    private const string Switch = "Switch<Lane>#0";
    private const string FastShip = "Switch<Lane>#0/Fast/Ship#0";
    private const string SlowShip = "Switch<Lane>#0/Slow/Ship#0";

    private static readonly ChainGraph Graph = new(
        "Acme.RoutedTrain",
        "Order",
        "Receipt",
        [
            Node("Fetch#0", ChainStepKind.Chain),
            Node(Decide, ChainStepKind.Decide),
            Node(
                Switch,
                ChainStepKind.Switch,
                tracks:
                [
                    new ChainGraphTrack("Fast", null, false, [Node(FastShip, ChainStepKind.Chain)]),
                    new ChainGraphTrack("Slow", null, false, [Node(SlowShip, ChainStepKind.Chain)]),
                ]
            ),
            Node("Extract<Order, Total>#0", ChainStepKind.Extract),
            Node("IPay#0", ChainStepKind.IChain, opaque: true),
            Node("Finish#0", ChainStepKind.Chain),
        ],
        []
    );

    [Test]
    public void EachStep_LandsOnTheNodeItsIdNames_WithItsState()
    {
        var run = RunGraphs.Match(
            1,
            "Acme.IRoutedTrain",
            Graph,
            [
                Step(0, "Fetch#0"),
                Step(1, Decide, JunctionRunKind.Choice, answer: "Fast", replayed: true),
                Step(2, Switch, JunctionRunKind.Route, answer: "Fast", replayed: true),
                Step(3, FastShip),
                Step(4, "Finish#0", state: JunctionRunState.Failed),
            ]
        );

        run.HasGraph.Should().BeTrue();
        run.Hash.Should().Be(Graph.Hash);
        run.UnmatchedSteps.Should().BeEmpty();

        var nodes = run.Nodes.ToDictionary(n => n.Id);
        nodes["Fetch#0"].State.Should().Be(RunNodeState.Completed);
        nodes["Fetch#0"].Steps.Select(s => s.Position).Should().Equal(0);
        nodes[Decide].Steps.Select(s => s.Position).Should().Equal(1);
        nodes[Decide].Replayed.Should().BeTrue();

        var routing = nodes[Switch];
        routing.State.Should().Be(RunNodeState.Completed);
        routing.Steps.Select(s => s.Position).Should().Equal(2);
        routing.Replayed.Should().BeTrue();
        routing.TrackTaken.Should().Be("Fast");
        routing.Tracks[0].Taken.Should().BeTrue();
        routing.Tracks[0].Nodes[0].State.Should().Be(RunNodeState.Completed);
        routing.Tracks[1].Taken.Should().BeFalse();
        routing
            .Tracks[1]
            .Nodes[0]
            .State.Should()
            .Be(RunNodeState.Skipped, "the run took the other track");

        nodes["Extract<Order, Total>#0"].State.Should().Be(RunNodeState.NotRecorded);
        nodes["IPay#0"].State.Should().Be(RunNodeState.NotReached);
        nodes["IPay#0"].Opaque.Should().BeTrue();
        nodes["Finish#0"].State.Should().Be(RunNodeState.Failed);
    }

    [Test]
    public void AFailedQuestion_FailsItsNode_AndLeavesTheTracksNotReached()
    {
        var run = RunGraphs.Match(
            1,
            "t",
            Graph,
            [
                Step(0, "Fetch#0"),
                Step(1, Decide, JunctionRunKind.Choice, state: JunctionRunState.Failed),
            ]
        );

        run.Nodes[1].State.Should().Be(RunNodeState.Failed);
        var routing = run.Nodes[2];
        routing.State.Should().Be(RunNodeState.NotReached);
        routing.TrackTaken.Should().BeNull();
        routing
            .Tracks.SelectMany(t => t.Nodes)
            .Should()
            .OnlyContain(n => n.State == RunNodeState.NotReached);
    }

    [Test]
    public void ARouteWithNoAnswer_TakesTheOneTrackThatRan()
    {
        // The writer drops a row rather than hold up the run, so a route can be missing its
        // answer while the steps on its track are there.
        var run = RunGraphs.Match(
            1,
            "t",
            Graph,
            [
                Step(0, Switch, JunctionRunKind.Route),
                Step(1, SlowShip, state: JunctionRunState.InProgress),
            ]
        );

        var routing = run.Nodes[2];
        routing.TrackTaken.Should().Be("Slow");
        routing.Tracks[0].Nodes[0].State.Should().Be(RunNodeState.Skipped);
        routing.Tracks[1].Nodes[0].State.Should().Be(RunNodeState.InProgress);
    }

    [Test]
    public void AWithheldRoute_WithholdsEveryNodeAfterIt_RatherThanCallingItNotReached()
    {
        var afterTheRoute = Step(3, nodeId: null) with { NameWithheld = true };
        var run = RunGraphs.Match(
            1,
            "t",
            Graph,
            [
                Step(0, "Fetch#0"),
                Step(1, Decide, JunctionRunKind.Choice) with
                {
                    AnswerWithheld = true,
                },
                Step(2, Switch, JunctionRunKind.Route) with
                {
                    AnswerWithheld = true,
                },
                afterTheRoute,
            ]
        );

        var routing = run.Nodes[2];
        routing.TrackTaken.Should().BeNull();
        routing
            .Tracks.SelectMany(t => t.Nodes)
            .Should()
            .OnlyContain(n => n.State == RunNodeState.Withheld);
        run.Nodes.Skip(3).Should().OnlyContain(n => n.State == RunNodeState.Withheld);
        run.Nodes[0].State.Should().Be(RunNodeState.Completed);
        run.UnmatchedSteps.Should().Equal(afterTheRoute);
    }

    [Test]
    public void AStepWithNoNodeId_OrANodeTheGraphLacks_IsUnmatched()
    {
        var old = Step(0, nodeId: null);
        var gone = Step(1, "Removed#0");
        var kept = Step(2, "Fetch#0");

        var run = RunGraphs.Match(1, "t", Graph, [old, gone, kept]);

        run.UnmatchedSteps.Should().Equal(old, gone);
        run.Nodes[0].Steps.Should().Equal(kept);
    }

    [Test]
    public void WithoutAGraph_EveryStepIsUnmatched()
    {
        var steps = new[] { Step(0, "Fetch#0"), Step(1, nodeId: null) };

        var run = RunGraphs.Match(9, "Acme.IGone", null, steps, moreSteps: true);

        run.HasGraph.Should().BeFalse();
        run.Hash.Should().BeNull();
        run.Nodes.Should().BeEmpty();
        run.UnmatchedSteps.Should().Equal(steps);
        run.MoreSteps.Should().BeTrue();
        run.Train.Should().Be("Acme.IGone");
    }

    [Test]
    public void AStepWhoseNameIsWithheld_LosesItsNodeId_SinceTheIdNamesTheTrack()
    {
        var step = Step(0, FastShip);

        step.WithNameWithheld().NodeId.Should().BeNull();
        step.WithTrackWithheld().NodeId.Should().BeNull();
    }

    [Test]
    public void ARecordedRow_CarriesItsNodeId_UnlessItsNameIsWithheld()
    {
        var row = new JunctionRun
        {
            MetadataId = 1,
            Position = 3,
            Kind = JunctionRunKind.Junction,
            Name = "Ship",
            State = JunctionRunState.Completed,
            StartedAt = DateTime.UtcNow,
            NodeId = FastShip,
        };

        JunctionStep.From(row).NodeId.Should().Be(FastShip);

        row.NameWithheld = true;
        JunctionStep.From(row).NodeId.Should().BeNull("the id names the track the name hides");
    }

    private static ChainGraphNode Node(
        string id,
        ChainStepKind kind,
        bool opaque = false,
        IReadOnlyList<ChainGraphTrack>? tracks = null
    ) => new(id, kind, id, "Order", "Order", opaque, tracks ?? []);

    private static JunctionStep Step(
        int position,
        string? nodeId,
        JunctionRunKind kind = JunctionRunKind.Junction,
        JunctionRunState state = JunctionRunState.Completed,
        string? answer = null,
        bool replayed = false
    ) =>
        new(
            position,
            kind,
            nodeId ?? "Old",
            state,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            kind == JunctionRunKind.Junction ? null : "Lane",
            answer,
            null,
            replayed,
            null,
            false,
            null
        )
        {
            NodeId = nodeId,
        };
}
