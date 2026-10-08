using AwesomeAssertions;
using Trax.Api.DTOs;
using Trax.Api.Services.Runs;
using Trax.Core.Monad;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;

namespace Trax.Api.Tests;

/// <summary>
/// <c>RunGraphs.Match</c> on a <c>Parallel</c> step. Unlike a routing step's tracks, every
/// branch runs: no branch is passed over, each branch's nodes stand as the run recorded them, and
/// the step itself, which records nothing, stands where its branches do. A withheld route inside
/// one branch withholds that branch and everything after the join, never a sibling branch.
/// </summary>
[TestFixture]
public class RunGraphParallelTests
{
    private const string Parallel = "Parallel#0";
    private const string CoCitation = "Parallel#0/cocitation/ScoreCoCitation#0";
    private const string Decide = "Parallel#0/cocitation/Decide<ChoiceDecision<Lane>>#0";
    private const string Switch = "Parallel#0/cocitation/Switch<Lane>#0";
    private const string FastShip = "Parallel#0/cocitation/Switch<Lane>#0/Fast/Ship#0";
    private const string SlowShip = "Parallel#0/cocitation/Switch<Lane>#0/Slow/Ship#0";
    private const string Citations = "Parallel#0/citations/ScoreCitations#0";
    private const string Nested = "Parallel#0/citations/Parallel#0";
    private const string NestedA = "Parallel#0/citations/Parallel#0/a/CountA#0";
    private const string NestedB = "Parallel#0/citations/Parallel#0/b/CountB#0";

    private static readonly ChainGraph Graph = new(
        "Acme.RankingTrain",
        "Paper",
        "Ranking",
        [
            Node("Fetch#0", ChainStepKind.Chain),
            Node(
                Parallel,
                ChainStepKind.Parallel,
                [
                    Branch(
                        "cocitation",
                        Node(CoCitation, ChainStepKind.Chain),
                        Node(Decide, ChainStepKind.Decide),
                        Node(
                            Switch,
                            ChainStepKind.Switch,
                            [
                                new ChainGraphTrack(
                                    "Fast",
                                    null,
                                    false,
                                    [Node(FastShip, ChainStepKind.Chain)]
                                ),
                                new ChainGraphTrack(
                                    "Slow",
                                    null,
                                    false,
                                    [Node(SlowShip, ChainStepKind.Chain)]
                                ),
                            ]
                        )
                    ),
                    Branch(
                        "citations",
                        Node(Citations, ChainStepKind.Chain),
                        Node(
                            Nested,
                            ChainStepKind.Parallel,
                            [
                                Branch("a", Node(NestedA, ChainStepKind.Chain)),
                                Branch("b", Node(NestedB, ChainStepKind.Chain)),
                            ]
                        )
                    ),
                ]
            ),
            Node("Merge#0", ChainStepKind.Chain),
        ],
        []
    );

    [Test]
    public void EveryBranchIsTaken_AndEachBranchsNodesStandAsRecorded()
    {
        // Branches interleave in the timeline: positions follow the clock, not the declaration.
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, Citations),
            Step(2, CoCitation),
            Step(3, NestedB),
            Step(4, Decide, JunctionRunKind.Choice, answer: "Slow"),
            Step(5, NestedA),
            Step(6, Switch, JunctionRunKind.Route, answer: "Slow"),
            Step(7, SlowShip),
            Step(8, "Merge#0")
        );

        run.UnmatchedSteps.Should().BeEmpty();
        var parallel = run.Nodes[1];
        parallel.Kind.Should().Be(ChainStepKind.Parallel);
        parallel.State.Should().Be(RunNodeState.Completed);
        parallel.TrackTaken.Should().BeNull("a Parallel step runs every branch");
        parallel.Steps.Should().BeEmpty();
        parallel.Tracks.Select(t => t.Name).Should().Equal("cocitation", "citations");
        parallel.Tracks.Should().OnlyContain(t => t.Taken);

        var all = Flatten(run.Nodes);
        all[CoCitation].State.Should().Be(RunNodeState.Completed);
        all[Citations].State.Should().Be(RunNodeState.Completed);
        all[Nested].State.Should().Be(RunNodeState.Completed);
        all[Nested].Tracks.Should().OnlyContain(t => t.Taken);
        all[NestedA].State.Should().Be(RunNodeState.Completed);
        all[NestedB].State.Should().Be(RunNodeState.Completed);

        // A routing step inside a branch still takes one track and passes the others over.
        all[Switch].TrackTaken.Should().Be("Slow");
        all[SlowShip].State.Should().Be(RunNodeState.Completed);
        all[FastShip].State.Should().Be(RunNodeState.Skipped);
        all["Merge#0"].State.Should().Be(RunNodeState.Completed);
    }

    [Test]
    public void AFailedBranch_FailsTheStep_AndLeavesItsSiblingAsRecorded()
    {
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, Citations),
            Step(2, CoCitation, state: JunctionRunState.Failed),
            Step(3, NestedA),
            Step(4, NestedB)
        );

        var all = Flatten(run.Nodes);
        all[Parallel].State.Should().Be(RunNodeState.Failed);
        all[CoCitation].State.Should().Be(RunNodeState.Failed);
        all[Decide].State.Should().Be(RunNodeState.NotReached, "the branch stopped at its failure");
        all[FastShip]
            .State.Should()
            .Be(RunNodeState.NotReached, "nothing routed, so no track was passed over");
        all[Citations].State.Should().Be(RunNodeState.Completed);
        all[Nested].State.Should().Be(RunNodeState.Completed);
        all["Merge#0"].State.Should().Be(RunNodeState.NotReached);
    }

    [Test]
    public void ABranchCancelledBecauseItsSiblingFailed_IsCancelled_AndTheStepFailed()
    {
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, CoCitation, state: JunctionRunState.Cancelled),
            Step(2, Citations, state: JunctionRunState.Failed)
        );

        var all = Flatten(run.Nodes);
        all[CoCitation].State.Should().Be(RunNodeState.Cancelled);
        all[Citations].State.Should().Be(RunNodeState.Failed);
        all[Parallel]
            .State.Should()
            .Be(RunNodeState.Failed, "a failure outranks the cancellation it caused");
    }

    [Test]
    public void ACancelledRun_CancelsTheStep()
    {
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, CoCitation, state: JunctionRunState.Cancelled),
            Step(2, Citations, state: JunctionRunState.Cancelled)
        );

        run.Nodes[1].State.Should().Be(RunNodeState.Cancelled);
    }

    [Test]
    public void TheStep_IsInProgress_UntilEveryBranchHasFinished()
    {
        Match(Step(0, "Fetch#0"), Step(1, CoCitation, state: JunctionRunState.InProgress))
            .Nodes[1]
            .State.Should()
            .Be(RunNodeState.InProgress);

        // One branch is done and the other has nodes still to reach.
        var partly = Match(
            Step(0, "Fetch#0"),
            Step(1, Citations),
            Step(2, NestedA),
            Step(3, NestedB)
        );
        partly.Nodes[1].State.Should().Be(RunNodeState.InProgress);
        partly
            .Nodes[1]
            .Tracks.Should()
            .OnlyContain(t => t.Taken, "every branch has started with the step");
    }

    [Test]
    public void AStepNoBranchHasStarted_IsNotReached_AndNoBranchIsTaken()
    {
        var run = Match(Step(0, "Fetch#0"));

        var parallel = run.Nodes[1];
        parallel.State.Should().Be(RunNodeState.NotReached);
        parallel.Tracks.Should().OnlyContain(t => !t.Taken);
        Flatten(parallel.Tracks.SelectMany(t => t.Nodes))
            .Values.Should()
            .OnlyContain(n => n.State == RunNodeState.NotReached);
    }

    [Test]
    public void AFailedNestedBranch_FailsTheNestedStep_AndTheOuterOne()
    {
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, Citations),
            Step(2, NestedA, state: JunctionRunState.Failed),
            Step(3, NestedB, state: JunctionRunState.Cancelled),
            Step(4, CoCitation, state: JunctionRunState.Cancelled)
        );

        var all = Flatten(run.Nodes);
        all[Nested].State.Should().Be(RunNodeState.Failed);
        all[NestedB].State.Should().Be(RunNodeState.Cancelled);
        all[Parallel].State.Should().Be(RunNodeState.Failed);
    }

    [Test]
    public void AWithheldRouteInOneBranch_WithholdsThatBranchAndPastTheJoin_ButNotItsSibling()
    {
        var withheld = Step(5, nodeId: null) with { NameWithheld = true };
        var run = Match(
            Step(0, "Fetch#0"),
            Step(1, CoCitation),
            Step(2, Citations),
            Step(3, Decide, JunctionRunKind.Choice) with
            {
                AnswerWithheld = true,
            },
            Step(4, Switch, JunctionRunKind.Route) with
            {
                AnswerWithheld = true,
            },
            withheld
        );

        var all = Flatten(run.Nodes);
        all[Switch].TrackTaken.Should().BeNull();
        all[FastShip].State.Should().Be(RunNodeState.Withheld);
        all[SlowShip].State.Should().Be(RunNodeState.Withheld);

        // The sibling branch ran on its own path: what it has not recorded it has not reached.
        all[Citations].State.Should().Be(RunNodeState.Completed);
        all[NestedA].State.Should().Be(RunNodeState.NotReached);
        all[Nested].State.Should().Be(RunNodeState.NotReached);

        // Past the join, which branch's steps ran could give the answer away.
        all["Merge#0"].State.Should().Be(RunNodeState.Withheld);
        run.UnmatchedSteps.Should().Equal(withheld);
        all[Parallel]
            .State.Should()
            .Be(RunNodeState.InProgress, "the sibling has nodes still to reach");
    }

    [Test]
    public void AParallelStepOnATrackNotTaken_IsSkipped_WithEveryBranch()
    {
        const string Route = "Switch<Lane>#0";
        const string Inner = "Switch<Lane>#0/Slow/Parallel#0";
        var graph = new ChainGraph(
            "Acme.T",
            "Paper",
            "Ranking",
            [
                Node(
                    Route,
                    ChainStepKind.Switch,
                    [
                        Branch("Fast", Node($"{Route}/Fast/Ship#0", ChainStepKind.Chain)),
                        Branch(
                            "Slow",
                            Node(
                                Inner,
                                ChainStepKind.Parallel,
                                [Branch("x", Node($"{Inner}/x/X#0", ChainStepKind.Chain))]
                            )
                        ),
                    ]
                ),
            ],
            []
        );

        var run = RunGraphs.Match(
            1,
            "t",
            graph,
            [Step(0, Route, JunctionRunKind.Route, answer: "Fast"), Step(1, $"{Route}/Fast/Ship#0")]
        );

        var parallel = run.Nodes[0].Tracks[1].Nodes[0];
        parallel.State.Should().Be(RunNodeState.Skipped);
        parallel.Tracks[0].Taken.Should().BeFalse();
        parallel.Tracks[0].Nodes[0].State.Should().Be(RunNodeState.Skipped);
    }

    [Test]
    public void AStepsBranchPath_IsWithheldWithItsName()
    {
        var row = new JunctionRun
        {
            MetadataId = 1,
            Position = 0,
            Kind = JunctionRunKind.Junction,
            Name = "ScoreCoCitation",
            State = JunctionRunState.Completed,
            StartedAt = DateTime.UtcNow,
            NodeId = CoCitation,
        };

        JunctionStep.From(row).NodeId.Should().Be(CoCitation, "the id carries the branch already");
        (Step(0, CoCitation) with { BranchPath = "Parallel#0/cocitation" })
            .WithNameWithheld()
            .BranchPath.Should()
            .BeNull("a branch inside a track names the track");
    }

    private static RunGraph Match(params JunctionStep[] steps) =>
        RunGraphs.Match(1, "Acme.IRankingTrain", Graph, steps);

    private static Dictionary<string, RunGraphNode> Flatten(IEnumerable<RunGraphNode> nodes)
    {
        var all = new Dictionary<string, RunGraphNode>();
        Walk(nodes);
        return all;

        void Walk(IEnumerable<RunGraphNode> level)
        {
            foreach (var node in level)
            {
                all[node.Id] = node;
                foreach (var track in node.Tracks)
                    Walk(track.Nodes);
            }
        }
    }

    private static ChainGraphTrack Branch(string name, params ChainGraphNode[] nodes) =>
        new(name, null, false, nodes);

    private static ChainGraphNode Node(
        string id,
        ChainStepKind kind,
        IReadOnlyList<ChainGraphTrack>? tracks = null
    ) => new(id, kind, id, "Paper", "Paper", false, tracks ?? []);

    private static JunctionStep Step(
        int position,
        string? nodeId,
        JunctionRunKind kind = JunctionRunKind.Junction,
        JunctionRunState state = JunctionRunState.Completed,
        string? answer = null
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
            false,
            null,
            false,
            null
        )
        {
            NodeId = nodeId,
        };
}
