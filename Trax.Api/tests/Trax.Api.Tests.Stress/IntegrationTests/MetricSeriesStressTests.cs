using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>operations.metrics.series</c> over a million runs in one scope, on top of the shared seed:
/// one train, one manifest and its group each own a million runs spread over the last 30 days,
/// so a 30-day series would read all of them and a 7-day one about 230,000. Each range served must
/// stay inside the dashboard metrics budget; a range that cannot (30 days) is refused by
/// <c>OperationsService.MetricSeriesRefusal</c> rather than served slowly.
/// </summary>
/// <remarks>
/// The fixture adds its rows in <c>OneTimeSetUp</c> and deletes them in <c>OneTimeTearDown</c>,
/// so the other fixtures see the seed as the bulk seeder left it.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class MetricSeriesStressTests : StressTestSetup
{
    private const string Train = "Trax.Stress.Trains.ISeriesTrain";
    private const string GroupName = "series-stress-group";
    private const int Runs = 1_000_000;

    private long _manifestId;
    private long _groupId;

    [OneTimeSetUp]
    public async Task SeedSeries()
    {
        await ExecSqlAsync(DeleteSql);
        await ExecSqlAsync(
            $"""
            INSERT INTO trax.manifest_group (name) VALUES ('{GroupName}');
            INSERT INTO trax.manifest (external_id, name, manifest_group_id, schedule_type)
            SELECT 'series-stress-manifest', '{Train}', id, 'none'::trax.schedule_type
            FROM trax.manifest_group WHERE name = '{GroupName}';
            INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, manifest_id)
            SELECT 'series-' || g, '{Train}',
                   (ARRAY['completed','completed','completed','completed','completed','completed',
                          'completed','failed','failed','cancelled']::trax.train_state[])[1 + (g % 10)],
                   now() - ((g % 43200) * interval '1 minute'),
                   now() - ((g % 43200) * interval '1 minute') + ((1 + g % 600) * interval '1 second'),
                   (SELECT id FROM trax.manifest WHERE external_id = 'series-stress-manifest')
            FROM generate_series(1, {Runs}) g;
            ANALYZE trax.metadata;
            """
        );
        _manifestId = await ScalarAsync<long>(
            "SELECT id FROM trax.manifest WHERE external_id = 'series-stress-manifest'"
        );
        _groupId = await ScalarAsync<long>(
            $"SELECT id FROM trax.manifest_group WHERE name = '{GroupName}'"
        );
    }

    [OneTimeTearDown]
    public async Task RemoveSeries() => await ExecSqlAsync(DeleteSql);

    private const string DeleteSql = $"""
        DELETE FROM trax.metadata WHERE name = '{Train}';
        DELETE FROM trax.manifest WHERE external_id = 'series-stress-manifest';
        DELETE FROM trax.manifest_group WHERE name = '{GroupName}';
        """;

    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    [TestCase(MetricSeriesScope.Train, MetricSeriesRange.Last7Days)]
    [TestCase(MetricSeriesScope.Manifest, MetricSeriesRange.Last7Days)]
    [TestCase(MetricSeriesScope.Group, MetricSeriesRange.Last7Days)]
    [TestCase(MetricSeriesScope.Train, MetricSeriesRange.Last24Hours)]
    [TestCase(MetricSeriesScope.Train, MetricSeriesRange.LastHour)]
    public async Task Series_OverAMillionRuns_WithinBudget(
        MetricSeriesScope scope,
        MetricSeriesRange range
    )
    {
        OperationsService.MetricSeriesRefusal(scope, range).Should().BeNull();
        await MeasureAsync(
            $"operations.metrics.series({scope}, {range}) over {Runs:N0} runs",
            MetricsBudget,
            async (sp, ct) =>
            {
                var series = await new MetricsQueries().GetSeries(
                    Operations(sp),
                    ct,
                    scope,
                    range,
                    id: scope == MetricSeriesScope.Group ? _groupId : _manifestId,
                    name: Train
                );
                series.Buckets.Count.Should().BeLessThanOrEqualTo(OperationsService.MaxPageSize);
                series.Buckets.Sum(b => b.Completed).Should().BePositive();
            }
        );
    }

    /// <summary>
    /// The 30-day series reads every one of the million runs: measured at 560-660 ms for each
    /// scope, over the budget, so it is refused before anything is read.
    /// </summary>
    [TestCase(MetricSeriesScope.Train)]
    [TestCase(MetricSeriesScope.Manifest)]
    [TestCase(MetricSeriesScope.Group)]
    public async Task Series_OverThirtyDays_IsRefused(MetricSeriesScope scope)
    {
        OperationsService
            .MetricSeriesRefusal(scope, MetricSeriesRange.Last30Days)
            .Should()
            .NotBeNull();
        using var services = ServiceProvider.CreateScope();
        var read = () =>
            new MetricsQueries().GetSeries(
                Operations(services.ServiceProvider),
                CancellationToken.None,
                scope,
                MetricSeriesRange.Last30Days,
                id: _manifestId,
                name: Train
            );

        (await read.Should().ThrowAsync<HotChocolate.GraphQLException>())
            .Which.Errors.Single()
            .Code.Should()
            .Be(MetricsQueries.SeriesArgumentCode);
    }
}
