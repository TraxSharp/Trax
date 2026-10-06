using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;

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

    [OneTimeSetUp]
    public async Task SeedJunctionRuns()
    {
        var bulkRuns = Math.Min(JunctionRuns / StepsPerRun, Profile.Metadata - 1);
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
