using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.Runs;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.ChainVerification;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The run page's run graph: the train's declared nodes in order, each routing step's tracks nested
/// under it with the one the run took marked, each node's state as <c>RunGraphs.Match</c> placed the
/// run's steps on it, and the steps that match no node listed below. The page reads it through the
/// matching the API's <c>runGraph</c> uses, so the two show the same nodes in the same states.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class RunGraphViewTests
{
    private const string Train = "Acme.Shipping.IRoutedTrain";
    private const string Decide = "Decide<ChoiceDecision<Lane>>#0";
    private const string Switch = "Switch<Lane>#0";
    private const string FastShip = "Switch<Lane>#0/Fast/Ship#0";
    private const string SlowShip = "Switch<Lane>#0/Slow/Ship#0";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private static readonly ChainGraph Graph = new(
        "Acme.Shipping.RoutedTrain",
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
                    new ChainGraphTrack(
                        "Fast",
                        "Ship today",
                        false,
                        [Node(FastShip, ChainStepKind.Chain)]
                    ),
                    new ChainGraphTrack("Slow", null, true, [Node(SlowShip, ChainStepKind.Chain)]),
                ]
            ),
            Node("IPay#0", ChainStepKind.IChain, opaque: true),
            Node("Finish#0", ChainStepKind.Chain),
        ],
        []
    );

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private FixedChainGraphs _graphs = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _graphs = new FixedChainGraphs();
        _ctx.Services.AddSingleton<ITrainChainGraphs>(_graphs);
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void The_graph_nests_each_track_under_its_routing_step_and_marks_the_one_taken()
    {
        var view = Render([
            Step(0, "Fetch#0"),
            Step(1, Decide, JunctionRunKind.Choice, answer: "Fast", replayed: true),
            Step(2, Switch, JunctionRunKind.Route, answer: "Fast", replayed: true),
            Step(3, FastShip),
            Step(4, "Finish#0", JunctionRunState.Failed),
        ]);

        view.FindAll(".cs-rg-card > ol > li.cs-rg-node")
            .Select(n => n.GetAttribute("data-node-id"))
            .Should()
            .Equal("Fetch#0", Decide, Switch, "IPay#0", "Finish#0");

        var tracks = view.FindAll($"li[data-node-id='{Switch}'] li.cs-rg-track").ToList();
        tracks.Select(t => t.GetAttribute("data-track")).Should().Equal("Fast", "Slow");
        tracks[0].GetAttribute("data-taken").Should().Be("true");
        tracks[0].TextContent.Should().Contain("taken");
        tracks[1].GetAttribute("data-taken").Should().Be("false");
        tracks[1].TextContent.Should().Contain("fallback");

        StateOf(view, FastShip).Should().Be(nameof(RunNodeState.Completed));
        StateOf(view, SlowShip).Should().Be(nameof(RunNodeState.Skipped));
        view.Find($"li[data-node-id='{SlowShip}']").TextContent.Should().Contain("skipped");
        view.Find($"li[data-node-id='{Decide}'] .cs-rg-replayed").Should().NotBeNull();
    }

    [Test]
    public void A_failed_node_says_so_with_its_failure_class_and_exception_type()
    {
        var view = Render([Step(0, "Fetch#0"), Step(1, "Finish#0", JunctionRunState.Failed)]);

        StateOf(view, "Finish#0").Should().Be(nameof(RunNodeState.Failed));
        var failed = view.Find("li[data-node-id='Finish#0']");
        failed.TextContent.Should().Contain("failed");
        failed
            .QuerySelector(".cs-rg-failure")!
            .TextContent.Should()
            .Contain("Permanent")
            .And.Contain("TimeoutException");
        StateOf(view, Switch).Should().Be(nameof(RunNodeState.NotReached));
    }

    [Test]
    public void An_opaque_node_is_marked_as_decided_at_run_time()
    {
        var view = Render([]);

        view.Find("li[data-node-id='IPay#0'] .cs-rg-opaque").TextContent.Should().Contain("opaque");
        view.FindAll("li[data-node-id='Fetch#0'] .cs-rg-opaque").Should().BeEmpty();
    }

    [Test]
    public void Steps_that_match_no_node_are_listed_rather_than_dropped()
    {
        var view = Render([
            Step(0, "Fetch#0"),
            Step(1, nodeId: null, name: "Legacy"),
            Step(2, "Removed#0", name: "Removed"),
        ]);

        view.FindAll(".cs-rg-unmatched-step")
            .Select(s => s.GetAttribute("data-position"))
            .Should()
            .Equal("1", "2");
        view.Find(".cs-rg-unmatched").TextContent.Should().Contain("Legacy").And.Contain("Removed");
    }

    [Test]
    public void Without_a_graph_it_says_why_instead_of_drawing_one()
    {
        var view = _ctx.RenderComponent<RunGraphView>(p =>
            p.Add(x => x.Graph, RunGraphs.Match(1, Train, null, [Step(0, "Fetch#0")]))
        );

        view.Find(".cs-rg-none").TextContent.Should().Contain("not registered here");
        view.FindAll("li.cs-rg-node").Should().BeEmpty();
        view.FindAll(".cs-rg-unmatched").Should().BeEmpty("the timeline already lists every step");
    }

    [Test]
    public async Task The_run_page_draws_the_graph_of_a_registered_train()
    {
        _graphs.With(Train, Graph);
        var runId = await SeedRunAsync(Train);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () => StateOf(page, FastShip).Should().Be(nameof(RunNodeState.Completed)),
            WaitTimeout
        );
        page.FindAll(".cs-rg-none").Should().BeEmpty();
    }

    [Test]
    public async Task The_run_page_of_a_train_with_no_graph_says_so()
    {
        var runId = await SeedRunAsync("Acme.Gone.ITrain");

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () => page.Find(".cs-rg-none").TextContent.Should().Contain("no declared graph"),
            WaitTimeout
        );
    }

    [Test]
    public async Task The_run_page_shows_each_node_in_the_state_the_API_reports()
    {
        _graphs.With(Train, Graph);
        var runId = await SeedRunAsync(Train);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        var api = await new OperationsQueries().GetRunGraph(runId, _data, _graphs, default);

        var expected = Flatten(api!.Nodes).ToList();
        expected.Should().NotBeEmpty();
        page.WaitForAssertion(
            () =>
                page.FindAll("li.cs-rg-node")
                    .Select(n => (n.GetAttribute("data-node-id"), n.GetAttribute("data-state")))
                    .Should()
                    .Equal(expected),
            WaitTimeout
        );
        page.FindAll(".cs-rg-unmatched-step")
            .Select(s => s.GetAttribute("data-position"))
            .Should()
            .Equal(api.UnmatchedSteps.Select(s => s.Position.ToString()));
    }

    private const string Fork = "Parallel#0";
    private const string Left = "Parallel#0/left/ScoreLeft#0";
    private const string RightSwitch = "Parallel#0/right/Switch<Lane>#0";
    private const string RightFast = "Parallel#0/right/Switch<Lane>#0/Fast/Ship#0";
    private const string RightSlow = "Parallel#0/right/Switch<Lane>#0/Slow/Ship#0";
    private const string NestedFork = "Parallel#0/right/Parallel#0";
    private const string NestedX = "Parallel#0/right/Parallel#0/x/CountX#0";
    private const string NestedY = "Parallel#0/right/Parallel#0/y/CountY#0";

    private static readonly ChainGraph ParallelGraph = new(
        "Acme.Ranking.RankingTrain",
        "Paper",
        "Ranking",
        [
            Node("Fetch#0", ChainStepKind.Chain),
            Node(
                Fork,
                ChainStepKind.Parallel,
                tracks:
                [
                    new ChainGraphTrack("left", null, false, [Node(Left, ChainStepKind.Chain)]),
                    new ChainGraphTrack(
                        "right",
                        null,
                        false,
                        [
                            Node(
                                RightSwitch,
                                ChainStepKind.Switch,
                                tracks:
                                [
                                    new ChainGraphTrack(
                                        "Fast",
                                        null,
                                        false,
                                        [Node(RightFast, ChainStepKind.Chain)]
                                    ),
                                    new ChainGraphTrack(
                                        "Slow",
                                        null,
                                        false,
                                        [Node(RightSlow, ChainStepKind.Chain)]
                                    ),
                                ]
                            ),
                            Node(
                                NestedFork,
                                ChainStepKind.Parallel,
                                tracks:
                                [
                                    new ChainGraphTrack(
                                        "x",
                                        null,
                                        false,
                                        [Node(NestedX, ChainStepKind.Chain)]
                                    ),
                                    new ChainGraphTrack(
                                        "y",
                                        null,
                                        false,
                                        [Node(NestedY, ChainStepKind.Chain)]
                                    ),
                                ]
                            ),
                        ]
                    ),
                ]
            ),
            Node("Join#0", ChainStepKind.Chain),
        ],
        []
    );

    [Test]
    public void A_Parallel_steps_branches_sit_side_by_side_as_named_lanes()
    {
        var view = _ctx.RenderComponent<RunGraphView>(p =>
            p.Add(
                x => x.Graph,
                RunGraphs.Match(
                    1,
                    Train,
                    ParallelGraph,
                    [
                        Step(0, "Fetch#0"),
                        Step(1, Left),
                        Step(2, RightSwitch, JunctionRunKind.Route, answer: "Fast"),
                        Step(3, RightFast),
                        Step(4, NestedY),
                        Step(5, NestedX, JunctionRunState.Failed),
                    ]
                )
            )
        );

        var fork = view.Find($"li[data-node-id='{Fork}']");
        fork.GetAttribute("data-kind").Should().Be(nameof(ChainStepKind.Parallel));
        fork.GetAttribute("data-state").Should().Be(nameof(RunNodeState.Failed));

        var lanes = fork.QuerySelector(":scope > ul.cs-rg-lanes")!;
        lanes.GetAttribute("aria-label").Should().Contain("side by side");
        var branches = lanes.QuerySelectorAll(":scope > li.cs-rg-lane").ToList();
        branches.Select(b => b.GetAttribute("data-branch")).Should().Equal("left", "right");
        branches.Should().OnlyContain(b => b.GetAttribute("data-taken") == "true");
        branches[0].QuerySelector(".cs-rg-lane-name")!.TextContent.Should().Contain("left");
        fork.QuerySelectorAll(":scope > ul.cs-rg-tracks")
            .Should()
            .BeEmpty("a Parallel step's branches are not alternatives");

        // A routing step inside a branch keeps its tracks; a Parallel inside one has lanes of its own.
        branches[1]
            .QuerySelector($"li[data-node-id='{RightSwitch}'] > ul.cs-rg-tracks")
            .Should()
            .NotBeNull();
        StateOf(view, RightFast).Should().Be(nameof(RunNodeState.Completed));
        StateOf(view, RightSlow).Should().Be(nameof(RunNodeState.Skipped));
        view.FindAll($"li[data-node-id='{NestedFork}'] > ul.cs-rg-lanes > li.cs-rg-lane")
            .Select(b => b.GetAttribute("data-branch"))
            .Should()
            .Equal("x", "y");
        StateOf(view, NestedFork).Should().Be(nameof(RunNodeState.Failed));
        StateOf(view, NestedY).Should().Be(nameof(RunNodeState.Completed));
        StateOf(view, "Join#0").Should().Be(nameof(RunNodeState.NotReached));
    }

    [Test]
    public void A_Parallel_step_not_yet_started_draws_its_lanes_idle()
    {
        var view = _ctx.RenderComponent<RunGraphView>(p =>
            p.Add(x => x.Graph, RunGraphs.Match(1, Train, ParallelGraph, [Step(0, "Fetch#0")]))
        );

        var lanes = view.FindAll($"li[data-node-id='{Fork}'] > ul.cs-rg-lanes > li.cs-rg-lane");
        lanes.Should().HaveCount(2);
        lanes.Should().OnlyContain(l => l.GetAttribute("data-taken") == "false");
        lanes.Should().OnlyContain(l => l.ClassList.Contains("cs-rg-lane--idle"));
        StateOf(view, Fork).Should().Be(nameof(RunNodeState.NotReached));
    }

    [Test]
    public async Task The_run_page_shows_each_branch_node_in_the_state_the_API_reports()
    {
        _graphs.With(Train, ParallelGraph);
        var runId = await SeedRunAsync(
            Train,
            Row(0, JunctionRunKind.Junction, "Fetch", "Fetch#0", JunctionRunState.Completed),
            Row(1, JunctionRunKind.Junction, "ScoreLeft", Left, JunctionRunState.Completed),
            Row(2, JunctionRunKind.Route, "Lane", RightSwitch, JunctionRunState.Completed, "Slow"),
            Row(3, JunctionRunKind.Junction, "CountX", NestedX, JunctionRunState.Cancelled),
            Row(4, JunctionRunKind.Junction, "Ship", RightSlow, JunctionRunState.Failed)
        );

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        var api = await new OperationsQueries().GetRunGraph(runId, _data, _graphs, default);

        var expected = Flatten(api!.Nodes).ToList();
        expected.Should().Contain((Fork, nameof(RunNodeState.Failed)));
        page.WaitForAssertion(
            () =>
                page.FindAll("li.cs-rg-node")
                    .Select(n => (n.GetAttribute("data-node-id"), n.GetAttribute("data-state")))
                    .Should()
                    .Equal(expected),
            WaitTimeout
        );
    }

    private static IEnumerable<(string?, string?)> Flatten(IEnumerable<RunGraphNode> nodes) =>
        nodes.SelectMany(n =>
            new[] { ((string?)n.Id, (string?)n.State.ToString()) }.Concat(
                n.Tracks.SelectMany(t => Flatten(t.Nodes))
            )
        );

    private IRenderedComponent<RunGraphView> Render(IReadOnlyList<JunctionStep> steps) =>
        _ctx.RenderComponent<RunGraphView>(p =>
            p.Add(x => x.Graph, RunGraphs.Match(1, Train, Graph, steps))
        );

    private static string? StateOf(IRenderedFragment view, string nodeId) =>
        view.Find($"li[data-node-id='{nodeId}']").GetAttribute("data-state");

    private Task<long> SeedRunAsync(string train) =>
        SeedRunAsync(
            train,
            Row(0, JunctionRunKind.Junction, "Fetch", "Fetch#0", JunctionRunState.Completed),
            Row(1, JunctionRunKind.Choice, "Lane", Decide, JunctionRunState.Completed, "Fast"),
            Row(2, JunctionRunKind.Route, "Lane", Switch, JunctionRunState.Completed, "Fast"),
            Row(3, JunctionRunKind.Junction, "Ship", FastShip, JunctionRunState.Completed),
            Row(4, JunctionRunKind.Junction, "Legacy", null, JunctionRunState.Completed),
            Row(5, JunctionRunKind.Junction, "Finish", "Finish#0", JunctionRunState.Failed)
        );

    private async Task<long> SeedRunAsync(string train, params JunctionRun[] rows)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = train,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = TrainState.Failed;
        await db.Track(run);
        await db.SaveChanges(default);

        foreach (var row in rows)
        {
            row.MetadataId = run.Id;
            db.JunctionRuns.Add(row);
        }
        await db.SaveChanges(default);
        return run.Id;
    }

    private static JunctionRun Row(
        int position,
        JunctionRunKind kind,
        string name,
        string? nodeId,
        JunctionRunState state,
        string? answer = null
    ) =>
        new()
        {
            Position = position,
            Kind = kind,
            Name = name,
            State = state,
            StartedAt = DateTime.UtcNow,
            EndedAt = kind == JunctionRunKind.Junction ? DateTime.UtcNow : null,
            QuestionKey = kind == JunctionRunKind.Junction ? null : name,
            Answer = answer,
            NodeId = nodeId,
            FailureClass = state == JunctionRunState.Failed ? FailureClass.Permanent : null,
            FailureException = state == JunctionRunState.Failed ? nameof(TimeoutException) : null,
        };

    private static ChainGraphNode Node(
        string id,
        ChainStepKind kind,
        bool opaque = false,
        IReadOnlyList<ChainGraphTrack>? tracks = null
    ) => new(id, kind, id, "Order", "Order", opaque, tracks ?? []);

    private static JunctionStep Step(
        int position,
        string? nodeId,
        JunctionRunState state = JunctionRunState.Completed,
        string? name = null
    ) => Step(position, nodeId, JunctionRunKind.Junction, state: state, name: name);

    private static JunctionStep Step(
        int position,
        string? nodeId,
        JunctionRunKind kind,
        JunctionRunState state = JunctionRunState.Completed,
        string? answer = null,
        bool replayed = false,
        string? name = null
    ) =>
        new(
            position,
            kind,
            name ?? nodeId ?? "Old",
            state,
            DateTime.UtcNow,
            null,
            null,
            state == JunctionRunState.Failed ? FailureClass.Permanent : null,
            state == JunctionRunState.Failed ? nameof(TimeoutException) : null,
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
