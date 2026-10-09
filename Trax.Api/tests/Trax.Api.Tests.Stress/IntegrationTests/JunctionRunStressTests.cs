using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.Runs;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Core.Monad;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>operations.junctionRuns</c> reads one run's recorded steps a page at a time, so its cost
/// must not grow with how many steps every other run has recorded. Measured over the seeded
/// metadata with millions of <c>trax.junction_run</c> rows spread across hundreds of thousands of
/// runs, and one run long enough for full pages.
/// </summary>
/// <remarks>
/// The base seed does not write steps, so this fixture adds them once and keeps them: it reseeds
/// only when the table holds fewer than <c>TRAX_STRESS_JUNCTION_RUNS</c> rows (default 2,000,000).
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class JunctionRunStressTests : StressTestSetup
{
    private const int StepsPerRun = 5;
    private const int LongRunSteps = 1_200;
    private const int Page = 500;

    private static readonly long JunctionRuns =
        long.TryParse(Environment.GetEnvironmentVariable("TRAX_STRESS_JUNCTION_RUNS"), out var n)
        && n > 0
            ? n
            : 2_000_000;

    /// <summary>The run with enough steps for several full pages: the newest seeded run.</summary>
    private static long LongRun => Profile.Metadata;

    /// <summary>
    /// A run of a wide <c>Parallel</c> step, every step on a node: the one before the newest.
    /// </summary>
    private static long ParallelRun => Profile.Metadata - 1;

    private const int Branches = 25;
    private const int StepsPerBranch = 10;
    private const int StepsPerNestedBranch = 5;

    /// <summary>Each branch's own steps and both of its nested branches', which fill one read.</summary>
    private const int ParallelSteps = Branches * (StepsPerBranch + 2 * StepsPerNestedBranch);

    [OneTimeSetUp]
    public async Task SeedJunctionRuns()
    {
        await SeedBulkJunctionRuns();
        await SeedParallelRun();
    }

    private async Task SeedBulkJunctionRuns()
    {
        var bulkRuns = Math.Min(JunctionRuns / StepsPerRun, Profile.Metadata - 2);
        var target = bulkRuns * StepsPerRun + LongRunSteps;
        if (await ScalarAsync<long>("SELECT count(*) FROM trax.junction_run") >= target)
            return;

        TestContext.Progress.WriteLine(
            $"[seed] junction_run: {bulkRuns:N0} runs x {StepsPerRun} steps + one run of {LongRunSteps:N0}"
        );
        await ExecSqlAsync("TRUNCATE trax.junction_run RESTART IDENTITY");
        // Each run's steps: a junction, a question, its route, and two junctions on the track, the
        // shape a routed run records.
        await ExecSqlAsync(
            $"""
            INSERT INTO trax.junction_run
                (metadata_id, position, kind, name, state, started_at, ended_at, question_key,
                 answer, confidence, track_position)
            SELECT m, p,
                   (CASE p WHEN 1 THEN 'choice' WHEN 2 THEN 'route' ELSE 'junction' END)::trax.junction_run_kind,
                   'Step' || p,
                   'completed'::trax.junction_run_state,
                   now() - (m % 20160) * interval '1 minute',
                   now() - (m % 20160) * interval '1 minute' + interval '5 milliseconds',
                   CASE WHEN p IN (1, 2) THEN 'Lane' END,
                   CASE WHEN p IN (1, 2) THEN 'Fast' END,
                   CASE WHEN p = 1 THEN 0.8 END,
                   CASE WHEN p > 2 THEN 2 END
            FROM generate_series(1, {bulkRuns}) AS m, generate_series(0, {StepsPerRun - 1}) AS p
            """
        );
        await ExecSqlAsync(
            $"""
            INSERT INTO trax.junction_run
                (metadata_id, position, kind, name, state, started_at, ended_at)
            SELECT {LongRun}, p, 'junction'::trax.junction_run_kind, 'Step' || p,
                   'completed'::trax.junction_run_state,
                   now() - interval '1 hour' + p * interval '1 second',
                   now() - interval '1 hour' + p * interval '1 second' + interval '5 milliseconds'
            FROM generate_series(0, {LongRunSteps - 1}) AS p
            """
        );
        await ExecSqlAsync("ANALYZE trax.junction_run");
    }

    // The branches' steps interleave, as branches running side by side record them: position p is
    // branch p % 25's step p / 25, its own ten first and then five in each nested branch.
    private async Task SeedParallelRun()
    {
        var placed = await ScalarAsync<long>(
            $"SELECT count(*) FROM trax.junction_run WHERE metadata_id = {ParallelRun} AND node_id IS NOT NULL"
        );
        if (placed == ParallelSteps)
            return;

        TestContext.Progress.WriteLine(
            $"[seed] junction_run: one Parallel run of {Branches} branches, {ParallelSteps} steps"
        );
        await ExecSqlAsync($"DELETE FROM trax.junction_run WHERE metadata_id = {ParallelRun}");
        await ExecSqlAsync(
            $"""
            INSERT INTO trax.junction_run
                (metadata_id, position, kind, name, state, started_at, ended_at, node_id)
            SELECT {ParallelRun}, p, 'junction'::trax.junction_run_kind, 'Step' || p,
                   'completed'::trax.junction_run_state,
                   now() - interval '1 hour' + p * interval '1 second',
                   now() - interval '1 hour' + p * interval '1 second' + interval '5 milliseconds',
                   'Parallel#0/b' || (p % {Branches}) || '/' ||
                   CASE WHEN p / {Branches} < {StepsPerBranch}
                        THEN 'Step' || (p / {Branches}) || '#0'
                        ELSE 'Parallel#0/'
                             || CASE WHEN p / {Branches} - {StepsPerBranch} < {StepsPerNestedBranch} THEN 'x' ELSE 'y' END
                             || '/Step' || ((p / {Branches} - {StepsPerBranch}) % {StepsPerNestedBranch}) || '#0'
                   END
            FROM generate_series(0, {ParallelSteps - 1}) AS p
            """
        );
        await ExecSqlAsync("ANALYZE trax.junction_run");
    }

    [Test]
    public async Task JunctionRuns_FirstFullPage_AtScale_WithinBudget()
    {
        await MeasureAsync(
            $"operations.junctionRuns (first page of {Page})",
            ListBudget,
            async (sp, ct) =>
            {
                var steps = await new OperationsQueries().GetJunctionRuns(
                    LongRun,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    ct,
                    take: Page
                );
                steps.Should().HaveCount(Page);
                steps[0].Position.Should().Be(0);
                steps[^1].Position.Should().Be(Page - 1);
            }
        );
    }

    [Test]
    public async Task JunctionRuns_NextFullPageByPosition_AtScale_WithinBudget()
    {
        await MeasureAsync(
            $"operations.junctionRuns (page of {Page} after position {Page - 1})",
            ListBudget,
            async (sp, ct) =>
            {
                var steps = await new OperationsQueries().GetJunctionRuns(
                    LongRun,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    ct,
                    afterPosition: Page - 1,
                    take: Page
                );
                steps.Should().HaveCount(Page);
                steps[0].Position.Should().Be(Page);
            }
        );
    }

    [Test]
    public async Task RunGraph_LongRun_AtScale_WithinBudget()
    {
        await MeasureAsync(
            $"operations.runGraph (a run of {LongRunSteps} steps, the first {RunGraphs.MaxSteps} read)",
            ListBudget,
            async (sp, ct) =>
            {
                var graph = await new OperationsQueries().GetRunGraph(
                    LongRun,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    new EveryTrainHasOneGraph(),
                    ct
                );
                graph!.HasGraph.Should().BeTrue();
                graph.MoreSteps.Should().BeTrue();
                (graph.Nodes.Sum(n => n.Steps.Count) + graph.UnmatchedSteps.Count)
                    .Should()
                    .Be(RunGraphs.MaxSteps);
            }
        );
    }

    [Test]
    public async Task RunGraph_ShortRun_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.runGraph (a five-step run)",
            ListBudget,
            async (sp, ct) =>
            {
                var graph = await new OperationsQueries().GetRunGraph(
                    1,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    new EveryTrainHasOneGraph(),
                    ct
                );
                graph!.MoreSteps.Should().BeFalse();
                (graph.Nodes.Sum(n => n.Steps.Count) + graph.UnmatchedSteps.Count)
                    .Should()
                    .Be(StepsPerRun);
            }
        );
    }

    [Test]
    public async Task RunGraph_WideParallelRun_AtScale_WithinBudget()
    {
        await MeasureAsync(
            $"operations.runGraph (a Parallel run of {Branches} branches, each with a nested Parallel, {ParallelSteps} steps)",
            ListBudget,
            async (sp, ct) =>
            {
                var graph = await new OperationsQueries().GetRunGraph(
                    ParallelRun,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    new EveryTrainRunsInParallel(),
                    ct
                );
                graph!.UnmatchedSteps.Should().BeEmpty();
                var fork = graph.Nodes.Should().ContainSingle().Subject;
                fork.State.Should().Be(RunNodeState.Completed);
                fork.Tracks.Should().HaveCount(Branches).And.OnlyContain(b => b.Taken);
                Placed(graph.Nodes).Should().Be(ParallelSteps);
            }
        );
    }

    private static int Placed(IEnumerable<RunGraphNode> nodes) =>
        nodes.Sum(n => n.Steps.Count + n.Tracks.Sum(t => Placed(t.Nodes)));

    /// <summary>
    /// A graph for every train: one <c>Parallel</c> step of 25 branches, each with ten steps and a
    /// nested <c>Parallel</c> of two branches of five.
    /// </summary>
    private sealed class EveryTrainRunsInParallel : ITrainChainGraphs
    {
        private static readonly ChainGraph Graph = new(
            "Acme.Train",
            "In",
            "Out",
            [
                Fork(
                    "Parallel#0",
                    Enumerable
                        .Range(0, Branches)
                        .Select(k => $"b{k}")
                        .Select(name => new ChainGraphTrack(
                            name,
                            null,
                            false,
                            [
                                .. Steps($"Parallel#0/{name}/", StepsPerBranch),
                                Fork(
                                    $"Parallel#0/{name}/Parallel#0",
                                    ["x", "y"],
                                    StepsPerNestedBranch
                                ),
                            ]
                        ))
                        .ToList()
                ),
            ],
            []
        );

        private static ChainGraphNode Fork(string id, IReadOnlyList<ChainGraphTrack> branches) =>
            new(id, ChainStepKind.Parallel, null, null, null, false, branches);

        private static ChainGraphNode Fork(string id, string[] names, int steps) =>
            Fork(
                id,
                names
                    .Select(n => new ChainGraphTrack(n, null, false, Steps($"{id}/{n}/", steps)))
                    .ToList()
            );

        private static List<ChainGraphNode> Steps(string prefix, int count) =>
            Enumerable
                .Range(0, count)
                .Select(j => new ChainGraphNode(
                    $"{prefix}Step{j}#0",
                    ChainStepKind.Chain,
                    $"Step{j}",
                    "In",
                    "In",
                    false,
                    []
                ))
                .ToList();

        public ChainGraph? Find(string train) => Graph;
    }

    /// <summary>A graph for every train: one junction step, and a switch with two tracks.</summary>
    private sealed class EveryTrainHasOneGraph : ITrainChainGraphs
    {
        private static readonly ChainGraph Graph = new(
            "Acme.Train",
            "In",
            "Out",
            [
                new ChainGraphNode("Step0#0", ChainStepKind.Chain, "Step0", "In", "In", false, []),
                new ChainGraphNode(
                    "Switch<Lane>#0",
                    ChainStepKind.Switch,
                    null,
                    "In",
                    "Taken<Lane>",
                    false,
                    [
                        new ChainGraphTrack("Fast", null, false, []),
                        new ChainGraphTrack("Slow", null, false, []),
                    ]
                ),
            ],
            []
        );

        public ChainGraph? Find(string train) => Graph;
    }

    [Test]
    public async Task JunctionRuns_ShortRun_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.junctionRuns (a five-step run)",
            ListBudget,
            async (sp, ct) =>
            {
                var steps = await new OperationsQueries().GetJunctionRuns(
                    1,
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    ct
                );
                steps.Should().HaveCount(StepsPerRun);
            }
        );
    }
}
