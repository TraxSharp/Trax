using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Api.Tests;

/// <summary>
/// Every paged read on the operations surface clamps <c>take</c> to 1 through
/// <see cref="OperationsPageBounds.MaxPageSize"/> and treats a negative <c>skip</c> as 0.
/// The table is seeded with one row more than the cap, so a request for more returns exactly
/// the cap. Shares <c>trax_api_operations</c> with <see cref="OperationsQueriesTests"/>: it
/// seeds once and every other fixture truncates in its own set-up. The decision is
/// <c>docs/adr/0017-an-operations-page-is-at-most-500-rows.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0017-an-operations-page-is-at-most-500-rows.md")]
public class OperationsPageBoundsTests
{
    private const int Rows = OperationsPageBounds.MaxPageSize + 1;

    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private const string Adr = "docs/adr/0017-an-operations-page-is-at-most-500-rows.md";

    private static string Because(string rule) => $"{rule} (see {Adr})";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;
    private long _parentId;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();

        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            await ((DbContext)db).Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE trax.log, trax.work_queue, trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
            );
        }

        await Seed();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private async Task Seed()
    {
        Manifest manifest;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var groups = new List<ManifestGroup>();
            for (var i = 0; i < Rows; i++)
            {
                var grp = new ManifestGroup
                {
                    Name = $"group-{i}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                groups.Add(grp);
                await db.Track(grp);
            }
            await db.SaveChanges(default);

            manifest = Manifest.Create(new CreateManifest { Name = typeof(PagedFakeTrain) });
            manifest.ManifestGroupId = groups[0].Id;
            await db.Track(manifest);
            for (var i = 1; i < Rows; i++)
            {
                var m = Manifest.Create(new CreateManifest { Name = typeof(PagedFakeTrain) });
                m.ManifestGroupId = groups[0].Id;
                await db.Track(m);
            }
            await db.SaveChanges(default);
        }

        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var parent = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.Parent",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            await db.Track(parent);
            await db.SaveChanges(default);
            _parentId = parent.Id;

            for (var i = 0; i < Rows; i++)
            {
                var child = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = "Trax.X.Child",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                child.ParentId = _parentId;
                await db.Track(child);

                await db.Track(
                    DeadLetter.Create(
                        new CreateDeadLetter
                        {
                            Manifest = manifest,
                            Reason = $"failure-{i}",
                            RetryCount = 3,
                        }
                    )
                );

                await db.Track(
                    WorkQueue.Create(new CreateWorkQueue { TrainName = $"Trax.Tests.IFake{i}" })
                );
            }
            await db.SaveChanges(default);

            await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) SELECT {_parentId}, g, 'information'::trax.log_level, 'msg', 'Test' FROM generate_series(1, {Rows}) g"
            );
        }
    }

    private static IEnumerable<TestCaseData> Queries()
    {
        yield return Case(
            "manifests",
            (f, skip, take) =>
                new OperationsQueries().GetManifests(f, default, skip: skip, take: take)
        );
        yield return Case(
            "executions",
            (f, skip, take) =>
                new OperationsQueries().GetExecutions(f, default, skip: skip, take: take)
        );
        yield return Case(
            "logs",
            (f, skip, take) => new LogQueries().GetLogs(f, default, skip: skip, take: take)
        );
        yield return Case(
            "deadLetters",
            (f, skip, take) =>
                new DeadLetterQueries().GetDeadLetters(f, default, skip: skip, take: take)
        );
        yield return Case(
            "workQueues",
            (f, skip, take) =>
                new WorkQueueQueries().GetWorkQueues(f, default, skip: skip, take: take)
        );
        yield return Case(
            "manifestGroups",
            (f, skip, take) =>
                new ManifestGroupQueries().GetGroups(f, default, skip: skip, take: take)
        );
    }

    private static TestCaseData Case<T>(
        string name,
        Func<IDataContextProviderFactory, int, int, Task<PagedResult<T>>> query
    ) =>
        new TestCaseData(
            new Func<IDataContextProviderFactory, int, int, Task<(int Count, int Skip, int Take)>>(
                async (f, skip, take) =>
                {
                    var page = await query(f, skip, take);
                    return (page.Items.Count, page.Skip, page.Take);
                }
            )
        ).SetArgDisplayNames(name);

    [TestCaseSource(nameof(Queries))]
    public async Task Take_AboveTheCap_ReturnsAtMostTheCap(
        Func<IDataContextProviderFactory, int, int, Task<(int Count, int Skip, int Take)>> query
    )
    {
        var page = await query(_factory, 0, 100_000);

        page.Count.Should()
            .Be(OperationsPageBounds.MaxPageSize, Because("caps an operations page at 500 rows"));
        page.Take.Should()
            .Be(OperationsPageBounds.MaxPageSize, Because("caps an operations page at 500 rows"));
    }

    [TestCaseSource(nameof(Queries))]
    public async Task Take_ZeroOrNegative_ReturnsOneRow(
        Func<IDataContextProviderFactory, int, int, Task<(int Count, int Skip, int Take)>> query
    )
    {
        (await query(_factory, 0, 0))
            .Should()
            .Be((1, 0, 1), Because("clamps take to 1..500 and skip to 0 or more"));
        (await query(_factory, 0, -5))
            .Should()
            .Be((1, 0, 1), Because("clamps take to 1..500 and skip to 0 or more"));
    }

    [TestCaseSource(nameof(Queries))]
    public async Task Skip_Negative_IsTreatedAsZero(
        Func<IDataContextProviderFactory, int, int, Task<(int Count, int Skip, int Take)>> query
    )
    {
        (await query(_factory, -10, 3))
            .Should()
            .Be((3, 0, 3), Because("clamps take to 1..500 and skip to 0 or more"));
    }

    [Test]
    public async Task ExecutionChildren_TakeAboveTheCap_ReturnsAtMostTheCap()
    {
        var page = await new OperationsQueries().GetExecutionChildren(
            _parentId,
            _factory,
            default,
            take: 100_000
        );

        page.Items.Should()
            .HaveCount(
                OperationsPageBounds.MaxPageSize,
                Because("caps an operations page at 500 rows")
            );
        page.Take.Should()
            .Be(OperationsPageBounds.MaxPageSize, Because("caps an operations page at 500 rows"));
    }

    [Test]
    public async Task ExecutionChildren_TakeZero_ReturnsOneRow()
    {
        var page = await new OperationsQueries().GetExecutionChildren(
            _parentId,
            _factory,
            default,
            take: 0
        );

        page.Items.Should().HaveCount(1, Because("clamps take to 1..500"));
        page.Take.Should().Be(1);
    }

    private class PagedFakeTrain { }
}
