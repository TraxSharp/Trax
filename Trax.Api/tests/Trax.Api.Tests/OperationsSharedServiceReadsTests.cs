using AwesomeAssertions;
using HotChocolate;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// The operations reads the dashboard also shows (a manifest's run counts, the groups list's
/// stats, the logs page) come from <see cref="IOperationsService"/>, so the API and the
/// dashboard answer from one implementation. Each test hands the resolver a service that
/// returns a known answer and checks that answer is what the field returns.
/// </summary>
[TestFixture]
public class OperationsSharedServiceReadsTests
{
    [Test]
    public async Task ManifestStats_AreTheServicesStats()
    {
        var lastRun = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var service = Substitute.For<IOperationsService>();
        service
            .GetManifestExecutionStatsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new ManifestExecutionStats(7, 10, 4, 3, 1, 1, 1, lastRun, lastRun));

        var stats = await new OperationsQueries().GetManifestStats(7, service, default);

        stats
            .Should()
            .BeEquivalentTo(
                new Trax.Api.DTOs.ManifestExecutionStats(7, 10, 4, 3, 1, 1, 1, lastRun, lastRun)
            );
    }

    [Test]
    public async Task GroupStats_AreTheServicesStats_ForEachDistinctIdOnce()
    {
        var service = Substitute.For<IOperationsService>();
        service
            .GetManifestGroupExecutionStatsAsync(
                Arg.Is<IReadOnlyCollection<long>>(ids =>
                    ids != null && ids.SequenceEqual(new long[] { 2, 1 })
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns([
                new ManifestGroupExecutionStats(2, 3, 9, 5, 2, 2, null),
                new ManifestGroupExecutionStats(1, 0, 0, 0, 0, 0, null),
            ]);

        var stats = await new ManifestGroupQueries().GetStats([2, 1, 2], service, default);

        stats.Select(s => s.GroupId).Should().Equal(2, 1);
        stats[0].TotalExecutions.Should().Be(9);
    }

    [Test]
    public async Task GroupStats_ForMoreIdsThanOneBatch_FailWithACodedError()
    {
        var service = Substitute.For<IOperationsService>();
        var ids = Enumerable.Range(1, OperationsService.MaxBatchSize + 1).Select(i => (long)i);

        var act = () => new ManifestGroupQueries().GetStats(ids.ToArray(), service, default);

        (await act.Should().ThrowAsync<GraphQLException>())
            .Which.Errors.Single()
            .Code.Should()
            .Be("TRAX_TOO_MANY_IDS");
        await service
            .DidNotReceiveWithAnyArgs()
            .GetManifestGroupExecutionStatsAsync(default!, default);
    }

    [Test]
    public async Task Logs_AreTheServicesPage_CountedByTheServiceWhenFiltered()
    {
        var service = Substitute.For<IOperationsService>();
        var record = new LogRecord(41, 3, 0, LogLevel.Warning, "Cat", "msg", null, null);
        service
            .GetLogsAsync(
                Arg.Is<LogQuery>(q =>
                    q != null
                    && q.MetadataId == 3
                    && q.MinimumLevel == LogLevel.Warning
                    && q.Take == 500
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new LogPage([record], 0, 500, 41));
        service.CountLogsAsync(Arg.Any<LogQuery>(), Arg.Any<CancellationToken>()).Returns(77);

        var page = await new LogQueries().GetLogs(
            service,
            Substitute.For<IDataContextProviderFactory>(),
            default,
            take: 9_999,
            metadataId: 3,
            minimumLevel: LogLevel.Warning
        );

        page.Items.Single().Id.Should().Be(41);
        page.TotalCount.Should().Be(77);
        page.IsEstimatedCount.Should().BeFalse();
        page.Take.Should().Be(500);
        page.NextCursor.Should().Be(41);
    }
}
