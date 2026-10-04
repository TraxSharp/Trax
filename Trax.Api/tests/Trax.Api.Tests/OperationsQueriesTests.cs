using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Fakes;
using Trax.Core.Exceptions;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// Direct tests for OperationsQueries and DeadLetterQueries against an InMemory
/// data context. Bypasses the GraphQL executor since these are plain async methods
/// returning DTOs — exercising them this way gives full line coverage of the
/// pagination / cursor / count-estimator branches.
/// </summary>
[TestFixture]
public class OperationsQueriesTests
{
    // Use a per-class Postgres database so CountEstimator's pg_class query works.
    // Each test isolates by cleaning the affected tables in SetUp.
    // Pool tuning matches the AuthE2E hardening from PR #41 after the same
    // class of CI flake here: aggressive Connection Pruning Interval=1 +
    // Idle Lifetime=1 forced every SetUp to pay TCP+auth and timed out under
    // contention. Pool Size=8 across the four test fixtures in this assembly
    // stays well under Postgres's default max_connections=100.
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    // No train is registered, so a stored input can't be read as its type and shows masked.
    private static ITrainDiscoveryService Discovery => Substitute.For<ITrainDiscoveryService>();

    private IOperationsService Operations =>
        new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            _factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>()
        );

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        // Clean the tables this fixture touches so each test starts fresh.
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (Microsoft.EntityFrameworkCore.DbContext)db;
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    private async Task SeedManifests(int count, long? groupId = null)
    {
        groupId ??= await SeedManifestGroup("default-group");
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
            m.IsEnabled = i % 2 == 0;
            m.ManifestGroupId = groupId.Value;
            await db.Track(m);
        }
        await db.SaveChanges(default);
    }

    private async Task<long> SeedManifestGroup(string name = "test-group")
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var grp = new ManifestGroup
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(grp);
        await db.SaveChanges(default);
        return grp.Id;
    }

    private async Task SeedExecutions(int count)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = $"Trax.X.Train{i}",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            await db.Track(meta);
        }
        await db.SaveChanges(default);
    }

    private async Task<long> SeedManifestInGroup(long groupId)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
        m.ManifestGroupId = groupId;
        await db.Track(m);
        await db.SaveChanges(default);
        return m.Id;
    }

    private async Task SeedExecutionsForManifest(
        long manifestId,
        int count,
        TrainState state = TrainState.Completed
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.Run",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            meta.ManifestId = manifestId;
            meta.TrainState = state;
            // Terminal runs carry an end time so last-successful-run aggregation has data.
            if (state is TrainState.Completed or TrainState.Failed or TrainState.Cancelled)
                meta.EndTime = DateTime.UtcNow;
            await db.Track(meta);
        }
        await db.SaveChanges(default);
    }

    private async Task<Manifest> SeedManifestForDeadLetter()
    {
        var groupId = await SeedManifestGroup();
        await using var db = await _factory.CreateDbContextAsync(default);
        var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
        m.ManifestGroupId = groupId;
        await db.Track(m);
        await db.SaveChanges(default);
        return m;
    }

    private async Task SeedDeadLetters(int count, Manifest manifest)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var dl = DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = $"failure-{i}",
                    RetryCount = 3,
                }
            );
            await db.Track(dl);
        }
        await db.SaveChanges(default);
    }

    [Test]
    public async Task GetManifests_NoData_ReturnsEmptyResult()
    {
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task GetManifests_PaginatesAndExposesCursor()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetManifests_AfterIdCursor_FiltersAndUsesExactCount()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();
        var first = await queries.GetManifests(_factory, default, take: 2);

        var page2 = await queries.GetManifests(
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(m => m.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
        page2.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetManifests_SkipPagination_HonorsSkip()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
        result.Skip.Should().Be(2);
    }

    [Test]
    public async Task GetManifest_ById_ReturnsRow()
    {
        await SeedManifests(2);
        var queries = new OperationsQueries();
        var first = (await queries.GetManifests(_factory, default)).Items.First();

        var fetched = await queries.GetManifest(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetManifest_MissingId_ReturnsNull()
    {
        var queries = new OperationsQueries();

        var fetched = await queries.GetManifest(99999, _factory, default);

        fetched.Should().BeNull();
    }

    [Test]
    public async Task GetExecutionDetail_ReturnsInputOutputAndStackTrace()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.DetailTrain",
                    ExternalId = "detail-ext",
                    Input = null,
                }
            );
            meta.Input = "{\"value\": 42}";
            meta.Output = "{\"result\": \"ok\"}";
            meta.StackTrace = "at Foo() line 1";
            meta.CurrentlyRunningJunction = "ChargeCard";
            await db.Track(meta);
            await db.SaveChanges(default);
            id = meta.Id;
        }

        var detail = await new OperationsQueries().GetExecutionDetail(id, _factory, default);

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(id);
        detail.ExternalId.Should().StartWith("detail-ext"); // external_id is CHAR(32), space-padded
        detail.Input.Should().Be("{\"value\": 42}");
        detail.Output.Should().Be("{\"result\": \"ok\"}");
        detail.StackTrace.Should().Be("at Foo() line 1");
        detail.CurrentlyRunningJunction.Should().Be("ChargeCard");
    }

    [Test]
    public async Task GetExecutionDetail_MissingId_ReturnsNull()
    {
        var detail = await new OperationsQueries().GetExecutionDetail(999999, _factory, default);

        detail.Should().BeNull();
    }

    [Test]
    public async Task GetExecutions_FilterByState_ReturnsOnlyMatchingWithExactCount()
    {
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            foreach (
                var state in new[] { TrainState.Completed, TrainState.Failed, TrainState.Failed }
            )
            {
                var meta = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = "Trax.X.FilterTrain",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                meta.TrainState = state;
                await db.Track(meta);
            }
            await db.SaveChanges(default);
        }

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            trainState: TrainState.Failed
        );

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(e => e.TrainState == TrainState.Failed);
        result.TotalCount.Should().Be(2);
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetExecutions_ReportsHowARunFailed_AndFiltersOnIt()
    {
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            foreach (var failureClass in new[] { FailureClass.Conflict, FailureClass.Transient })
            {
                var meta = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = "Trax.X.Classified",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                meta.TrainState = TrainState.Failed;
                meta.AddException(
                    new TrainException(
                        System.Text.Json.JsonSerializer.Serialize(
                            new TrainExceptionData
                            {
                                TrainName = "Classified",
                                TrainExternalId = meta.ExternalId,
                                Type = "SomeException",
                                Junction = "SomeJunction",
                                Message = "failed",
                                FailureClass = failureClass,
                            }
                        )
                    )
                );
                await db.Track(meta);
            }
            await db.SaveChanges(default);
        }

        // Rows the filter must leave out: a completed run and an unclassified failure.
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var completed = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.Classified",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            completed.TrainState = TrainState.Completed;
            var unclassified = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.Classified",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            unclassified.TrainState = TrainState.Failed;
            await db.Track(completed);
            await db.Track(unclassified);
            await db.SaveChanges(default);
        }

        var all = await new OperationsQueries().GetExecutions(_factory, default);
        all.Items.Select(e => e.FailureClass)
            .Should()
            .BeEquivalentTo(
                [
                    FailureClass.Conflict,
                    FailureClass.Transient,
                    FailureClass.Unclassified,
                    FailureClass.Unclassified,
                ],
                "the stored class is what the dashboard and API consumers read"
            );

        var conflicts = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            failureClass: FailureClass.Conflict
        );

        conflicts
            .Items.Should()
            .ContainSingle()
            .Which.FailureClass.Should()
            .Be(FailureClass.Conflict);
        conflicts.TotalCount.Should().Be(1, "the count covers the filter, not the table");
        conflicts.IsEstimatedCount.Should().BeFalse("a filtered page is counted exactly");
    }

    [Test]
    public async Task GetManifests_FilterByEnabled_ReturnsOnlyEnabled()
    {
        await SeedManifests(4); // SeedManifests toggles IsEnabled on even indexes -> 2 enabled

        var result = await new OperationsQueries().GetManifests(_factory, default, isEnabled: true);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(m => m.IsEnabled);
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetManifests_FilterByScheduleTypeAndName_NarrowsResults()
    {
        await SeedManifests(3); // all default to ScheduleType.None, same fake train name
        var q = new OperationsQueries();

        var bySchedule = await q.GetManifests(_factory, default, scheduleType: ScheduleType.None);
        var byOtherSchedule = await q.GetManifests(
            _factory,
            default,
            scheduleType: ScheduleType.Cron
        );
        var byName = await q.GetManifests(_factory, default, nameContains: "FakeTrain");
        var byMissingName = await q.GetManifests(
            _factory,
            default,
            nameContains: "no-such-manifest"
        );

        bySchedule.Items.Should().HaveCount(3);
        byOtherSchedule.Items.Should().BeEmpty();
        byName.Items.Should().HaveCount(3);
        byMissingName.Items.Should().BeEmpty();
    }

    [Test]
    public async Task GetExecutions_TimeRange_FiltersOnStartTime()
    {
        await SeedExecutions(3); // created ~now
        var q = new OperationsQueries();

        var included = await q.GetExecutions(
            _factory,
            default,
            startedAfter: DateTime.UtcNow.AddMinutes(-5)
        );
        var excluded = await q.GetExecutions(
            _factory,
            default,
            startedAfter: DateTime.UtcNow.AddMinutes(5)
        );
        // startedBefore: a window bounded on the upper side includes the just-created rows.
        var beforeIncluded = await q.GetExecutions(
            _factory,
            default,
            startedBefore: DateTime.UtcNow.AddMinutes(5)
        );
        var beforeExcluded = await q.GetExecutions(
            _factory,
            default,
            startedBefore: DateTime.UtcNow.AddMinutes(-5)
        );

        included.Items.Should().HaveCount(3);
        included.IsEstimatedCount.Should().BeFalse();
        excluded.Items.Should().BeEmpty();
        beforeIncluded.Items.Should().HaveCount(3);
        beforeExcluded.Items.Should().BeEmpty();
    }

    [Test]
    public async Task GetExecutions_FilterByTrainName_ReturnsOnlyMatch()
    {
        await SeedExecutions(3); // Trax.X.Train0, Train1, Train2

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            trainName: "Trax.X.Train1"
        );

        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Trax.X.Train1");
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetExecutions_HideAdminTrains_ExcludesAdminTrainRuns()
    {
        var adminName = AdminTrains.FullNames.First();
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            foreach (var name in new[] { adminName, adminName, "Trax.X.RegularTrain" })
            {
                var meta = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                await db.Track(meta);
            }
            await db.SaveChanges(default);
        }

        var all = await new OperationsQueries().GetExecutions(_factory, default);
        var visible = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            hideAdminTrains: true
        );

        all.Items.Should().HaveCount(3);
        visible.Items.Should().ContainSingle();
        visible.Items[0].Name.Should().Be("Trax.X.RegularTrain");
        visible.Items.Should().OnlyContain(e => !AdminTrains.FullNames.Contains(e.Name));
        visible.IsEstimatedCount.Should().BeFalse("hideAdminTrains forces an exact count");
    }

    [Test]
    public void GetAdminTrainNames_ReturnsCanonicalFullNames()
    {
        var names = new OperationsQueries().GetAdminTrainNames();

        names.Should().BeEquivalentTo(AdminTrains.FullNames);
        names.Should().Contain(n => n.EndsWith("IJobDispatcherTrain"));
    }

    [Test]
    public async Task GetHosts_RollsUpByInstance_WithRunningAndTotalCounts()
    {
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            // inst-1: two runs, one still in progress. inst-2: a single completed run.
            async Task Seed(string instance, string name, string env, TrainState state)
            {
                var m = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = "Trax.X.HostTrain",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                m.HostInstanceId = instance;
                m.HostName = name;
                m.HostEnvironment = env;
                m.TrainState = state;
                await db.Track(m);
            }

            await Seed("inst-1", "host-1", "Production", TrainState.Completed);
            await Seed("inst-1", "host-1", "Production", TrainState.InProgress);
            await Seed("inst-2", "host-2", "Development", TrainState.Completed);
            await db.SaveChanges(default);
        }

        var hosts = await new OperationsQueries().GetHosts(_factory, default);

        hosts.Should().HaveCount(2);
        var one = hosts.Single(h => h.InstanceId == "inst-1");
        one.Name.Should().Be("host-1");
        one.Environment.Should().Be("Production");
        one.TotalExecutions.Should().Be(2);
        one.CurrentlyRunning.Should().Be(1);
        var two = hosts.Single(h => h.InstanceId == "inst-2");
        two.TotalExecutions.Should().Be(1);
        two.CurrentlyRunning.Should().Be(0);
    }

    [Test]
    public async Task GetHosts_IgnoresRowsWithoutAHostInstance()
    {
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            for (var i = 0; i < 3; i++)
            {
                var m = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = $"Trax.X.NoHost{i}",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                // Metadata.Create stamps the running process's host by default; clear it so this
                // row represents pre-host-tracking data that the rollup must skip.
                m.HostInstanceId = null;
                m.HostName = null;
                m.HostEnvironment = null;
                await db.Track(m);
            }
            await db.SaveChanges(default);
        }

        var hosts = await new OperationsQueries().GetHosts(_factory, default);

        hosts.Should().BeEmpty();
    }

    [Test]
    public async Task GetTrainStats_ScopesToTrainAndAveragesCompletedDurations()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            async Task Seed(string name, TrainState state, DateTime? end)
            {
                var m = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                m.StartTime = start;
                m.TrainState = state;
                m.EndTime = end;
                await db.Track(m);
            }

            // Two completed runs (2s and 4s) → avg 3000ms. One failed. Plus another train, ignored.
            await Seed("Trax.X.StatTrain", TrainState.Completed, start.AddSeconds(2));
            await Seed("Trax.X.StatTrain", TrainState.Completed, start.AddSeconds(4));
            await Seed("Trax.X.StatTrain", TrainState.Failed, null);
            await Seed("Trax.X.OtherTrain", TrainState.Completed, start.AddSeconds(9));
            await db.SaveChanges(default);
        }

        var stats = await new OperationsQueries().GetTrainStats(
            "Trax.X.StatTrain",
            _factory,
            default
        );

        stats.TrainName.Should().Be("Trax.X.StatTrain");
        stats.Total.Should().Be(3);
        stats.Completed.Should().Be(2);
        stats.Failed.Should().Be(1);
        stats.AverageMilliseconds.Should().BeApproximately(3000, 0.001);
    }

    [Test]
    public async Task GetExecutions_OrderOldest_ReturnsAscendingIds()
    {
        await SeedExecutions(3);

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            order: SortOrder.Oldest
        );

        result.Items.Select(e => e.Id).Should().BeInAscendingOrder();
    }

    [Test]
    public async Task GetExecutions_FilterByManifestId_ReturnsOnlyThatManifestsRuns()
    {
        var groupId = await SeedManifestGroup("mgroup");
        var m1 = await SeedManifestInGroup(groupId);
        var m2 = await SeedManifestInGroup(groupId);
        await SeedExecutionsForManifest(m1, 3);
        await SeedExecutionsForManifest(m2, 2);

        var result = await new OperationsQueries().GetExecutions(_factory, default, manifestId: m1);

        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(e => e.ManifestId == m1);
        result.TotalCount.Should().Be(3);
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetExecutions_FilterByManifestId_NoMatches_ReturnsEmpty()
    {
        var groupId = await SeedManifestGroup("g");
        var m1 = await SeedManifestInGroup(groupId);
        await SeedExecutionsForManifest(m1, 2);

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            manifestId: 999999
        );

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Test]
    public async Task GetExecutions_FilterByManifestGroupId_ReturnsRunsForEveryManifestInGroup()
    {
        var groupA = await SeedManifestGroup("A");
        var groupB = await SeedManifestGroup("B");
        var a1 = await SeedManifestInGroup(groupA);
        var a2 = await SeedManifestInGroup(groupA);
        var b1 = await SeedManifestInGroup(groupB);
        await SeedExecutionsForManifest(a1, 2);
        await SeedExecutionsForManifest(a2, 3);
        await SeedExecutionsForManifest(b1, 4);

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            manifestGroupId: groupA
        );

        result.Items.Should().HaveCount(5); // a1(2) + a2(3), never b1
        result.Items.Should().OnlyContain(e => e.ManifestId == a1 || e.ManifestId == a2);
        result.TotalCount.Should().Be(5);
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetExecutions_ManifestIdAndState_Combine()
    {
        var groupId = await SeedManifestGroup("g");
        var m1 = await SeedManifestInGroup(groupId);
        await SeedExecutionsForManifest(m1, 2, TrainState.Completed);
        await SeedExecutionsForManifest(m1, 1, TrainState.Failed);

        var result = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            manifestId: m1,
            trainState: TrainState.Failed
        );

        result.Items.Should().ContainSingle();
        result.Items[0].TrainState.Should().Be(TrainState.Failed);
        result.Items[0].ManifestId.Should().Be(m1);
    }

    [Test]
    public async Task GetManifests_FilterByManifestGroupId_ReturnsOnlyGroupMembers()
    {
        var groupA = await SeedManifestGroup("A");
        var groupB = await SeedManifestGroup("B");
        await SeedManifestInGroup(groupA);
        await SeedManifestInGroup(groupA);
        await SeedManifestInGroup(groupB);

        var result = await new OperationsQueries().GetManifests(
            _factory,
            default,
            manifestGroupId: groupA
        );

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(m => m.ManifestGroupId == groupA);
        result.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetManifestStats_CountsByStateAndLastRun()
    {
        var groupId = await SeedManifestGroup("g");
        var m1 = await SeedManifestInGroup(groupId);
        await SeedExecutionsForManifest(m1, 3, TrainState.Completed);
        await SeedExecutionsForManifest(m1, 2, TrainState.Failed);
        await SeedExecutionsForManifest(m1, 1, TrainState.InProgress);

        var stats = await new OperationsQueries().GetManifestStats(m1, Operations, default);

        stats.ManifestId.Should().Be(m1);
        stats.Total.Should().Be(6);
        stats.Completed.Should().Be(3);
        stats.Failed.Should().Be(2);
        stats.InProgress.Should().Be(1);
        stats.Cancelled.Should().Be(0);
        stats.Pending.Should().Be(0);
        stats.LastRun.Should().NotBeNull();
        stats.LastSuccessfulRun.Should().NotBeNull(); // completed rows carry an end time
    }

    [Test]
    public async Task GetManifestStats_NoRuns_ReturnsZeros()
    {
        var groupId = await SeedManifestGroup("g");
        var m1 = await SeedManifestInGroup(groupId);

        var stats = await new OperationsQueries().GetManifestStats(m1, Operations, default);

        stats.Total.Should().Be(0);
        stats.Completed.Should().Be(0);
        stats.LastRun.Should().BeNull();
        stats.LastSuccessfulRun.Should().BeNull();
    }

    [Test]
    public async Task GetGroupStats_PerGroup_CountsManifestsAndExecutions_InRequestedOrder()
    {
        var groupA = await SeedManifestGroup("A");
        var groupB = await SeedManifestGroup("B");
        var a1 = await SeedManifestInGroup(groupA);
        var a2 = await SeedManifestInGroup(groupA);
        var b1 = await SeedManifestInGroup(groupB);
        await SeedExecutionsForManifest(a1, 2, TrainState.Completed);
        await SeedExecutionsForManifest(a2, 1, TrainState.Failed);
        await SeedExecutionsForManifest(b1, 3, TrainState.Completed);

        var stats = await new ManifestGroupQueries().GetStats(
            new[] { groupA, groupB },
            Operations,
            default
        );

        stats.Select(s => s.GroupId).Should().Equal(groupA, groupB); // requested order preserved
        var sa = stats.Single(s => s.GroupId == groupA);
        sa.ManifestCount.Should().Be(2);
        sa.TotalExecutions.Should().Be(3);
        sa.Completed.Should().Be(2);
        sa.Failed.Should().Be(1);
        sa.LastRun.Should().NotBeNull();
        var sb = stats.Single(s => s.GroupId == groupB);
        sb.ManifestCount.Should().Be(1);
        sb.TotalExecutions.Should().Be(3);
        sb.Completed.Should().Be(3);
    }

    [Test]
    public async Task GetGroupStats_EmptyGroupIds_ReturnsEmpty()
    {
        var stats = await new ManifestGroupQueries().GetStats(
            Array.Empty<long>(),
            Operations,
            default
        );

        stats.Should().BeEmpty();
    }

    [Test]
    public async Task GetGroupStats_GroupWithNoManifestsOrExecutions_ReturnsZeroRow()
    {
        var groupId = await SeedManifestGroup("empty");

        var stats = await new ManifestGroupQueries().GetStats(
            new[] { groupId },
            Operations,
            default
        );

        stats.Should().ContainSingle();
        stats[0].GroupId.Should().Be(groupId);
        stats[0].ManifestCount.Should().Be(0);
        stats[0].TotalExecutions.Should().Be(0);
        stats[0].LastRun.Should().BeNull();
    }

    [Test]
    public void GetEffects_MapsRegistryEntries_WithEnabledAndToggleableState()
    {
        var registry = Substitute.For<IEffectRegistry>();
        registry
            .GetAll()
            .Returns(
                new Dictionary<Type, bool>
                {
                    { typeof(FakeInput), true },
                    { typeof(FakeOutput), false },
                }
            );
        registry.IsToggleable(typeof(FakeInput)).Returns(true);
        registry.IsToggleable(typeof(FakeOutput)).Returns(false);

        var result = new OperationsQueries().GetEffects(registry, EmptyServices);

        result.Should().HaveCount(2);
        var enabled = result.Single(e => e.Name == nameof(FakeInput));
        enabled.Enabled.Should().BeTrue();
        enabled.Toggleable.Should().BeTrue();
        enabled.FullName.Should().Be(typeof(FakeInput).FullName);
        var disabled = result.Single(e => e.Name == nameof(FakeOutput));
        disabled.Enabled.Should().BeFalse();
        disabled.Toggleable.Should().BeFalse();
    }

    [Test]
    public void GetEffects_EmptyRegistry_ReturnsEmpty()
    {
        var registry = Substitute.For<IEffectRegistry>();
        registry.GetAll().Returns(new Dictionary<Type, bool>());

        new OperationsQueries().GetEffects(registry, EmptyServices).Should().BeEmpty();
    }

    [Test]
    public async Task GetManifestExclusions_ReturnsTypedWindows()
    {
        var groupId = await SeedManifestGroup("g");
        long manifestId;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
            m.ManifestGroupId = groupId;
            m.SetExclusions(
                new List<Exclusion>
                {
                    Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday),
                    Exclude.DateRange(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26)),
                    Exclude.TimeWindow(new TimeOnly(2, 0), new TimeOnly(4, 0)),
                }
            );
            await db.Track(m);
            await db.SaveChanges(default);
            manifestId = m.Id;
        }

        var result = await new OperationsQueries().GetManifestExclusions(
            manifestId,
            _factory,
            default
        );

        result.Should().HaveCount(3);
        result
            .Single(e => e.Type == ExclusionType.DaysOfWeek)
            .DaysOfWeek.Should()
            .BeEquivalentTo(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        var range = result.Single(e => e.Type == ExclusionType.DateRange);
        range.StartDate.Should().Be(new DateOnly(2026, 12, 24));
        range.EndDate.Should().Be(new DateOnly(2026, 12, 26));
        var window = result.Single(e => e.Type == ExclusionType.TimeWindow);
        window.StartTime.Should().Be(new TimeOnly(2, 0));
        window.EndTime.Should().Be(new TimeOnly(4, 0));
    }

    [Test]
    public async Task GetManifestExclusions_NoneOrMissing_ReturnsEmpty()
    {
        var groupId = await SeedManifestGroup("g");
        var m1 = await SeedManifestInGroup(groupId);

        (await new OperationsQueries().GetManifestExclusions(m1, _factory, default))
            .Should()
            .BeEmpty();
        (await new OperationsQueries().GetManifestExclusions(999999, _factory, default))
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task GetExecutionChildren_And_ChildCount_ReflectParentId()
    {
        await SeedExecutions(4);
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var ctx = (Microsoft.EntityFrameworkCore.DbContext)db;
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE trax.metadata SET parent_id = (SELECT MIN(id) FROM trax.metadata) "
                    + "WHERE id > (SELECT MIN(id) FROM trax.metadata)"
            );
        }

        long parentId;
        await using (var db = await _factory.CreateDbContextAsync(default))
            parentId = await db.Metadatas.MinAsync(m => m.Id);

        var q = new OperationsQueries();
        var children = await q.GetExecutionChildren(parentId, _factory, default);
        var detail = await q.GetExecutionDetail(parentId, _factory, default);

        children.Items.Should().HaveCount(3);
        children.TotalCount.Should().Be(3);
        children.Items.Should().OnlyContain(c => c.Id > parentId);
        detail!.ChildCount.Should().Be(3);
    }

    [Test]
    public async Task CancelExecution_FlagsInProgress_AndReturnsCount()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.CancelMe",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            meta.TrainState = TrainState.InProgress;
            await db.Track(meta);
            await db.SaveChanges(default);
            id = meta.Id;
        }

        var resp = await new OperationsMutations().CancelExecution(id, Operations, default);

        resp.Success.Should().BeTrue();
        resp.Count.Should().Be(1);
        await using (var db = await _factory.CreateDbContextAsync(default))
            (await db.Metadatas.FirstAsync(m => m.Id == id))
                .CancellationRequested.Should()
                .BeTrue();
    }

    [Test]
    public async Task CancelExecution_MissingOrTerminal_ReturnsZero()
    {
        var resp = await new OperationsMutations().CancelExecution(999999, Operations, default);

        resp.Success.Should().BeFalse();
        resp.Count.Should().Be(0);
    }

    [Test]
    public async Task UpdateManifest_PatchesMutableFields()
    {
        await SeedManifests(1);
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
            id = await db.Manifests.Select(m => m.Id).FirstAsync();

        var signal = new RecordingChangeSignal();
        var resp = await new OperationsMutations().UpdateManifest(
            id,
            new UpdateManifestInput(
                IsEnabled: false,
                MaxRetries: 9,
                Priority: 7,
                ScheduleType: ScheduleType.Cron,
                CronExpression: "0 0 * * *"
            ),
            _factory,
            signal,
            default
        );

        resp.Success.Should().BeTrue();
        signal.Domains.Should().ContainSingle().Which.Should().Be(ChangeDomain.Manifest);
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync(x => x.Id == id);
            m.IsEnabled.Should().BeFalse();
            m.MaxRetries.Should().Be(9);
            m.Priority.Should().Be(7);
            m.ScheduleType.Should().Be(ScheduleType.Cron);
            m.CronExpression.Should().Be("0 0 * * *");
        }
    }

    private async Task<long> SeedIntervalManifest()
    {
        await SeedManifests(1);
        await using var db = await _factory.CreateDbContextAsync(default);
        var seeded = await db.Manifests.FirstAsync();
        seeded.ScheduleType = ScheduleType.Interval;
        seeded.IntervalSeconds = 60;
        seeded.CronExpression = null;
        seeded.TimeoutSeconds = 30;
        seeded.MaxRetries = 2;
        await db.SaveChanges(default);
        return seeded.Id;
    }

    private async Task<OperationResponse> RefusedUpdate(long id, UpdateManifestInput input)
    {
        var signal = new RecordingChangeSignal();
        var resp = await new OperationsMutations().UpdateManifest(
            id,
            input,
            _factory,
            signal,
            default
        );

        resp.Success.Should().BeFalse(resp.Message);
        signal.Domains.Should().BeEmpty("a refused update changes nothing");
        await using var db = await _factory.CreateDbContextAsync(default);
        var m = await db.Manifests.AsNoTracking().FirstAsync(x => x.Id == id);
        m.ScheduleType.Should().Be(ScheduleType.Interval, "nothing is saved");
        m.IntervalSeconds.Should().Be(60, "nothing is saved");
        m.CronExpression.Should().BeNull("nothing is saved");
        m.TimeoutSeconds.Should().Be(30, "nothing is saved");
        m.MaxRetries.Should().Be(2, "nothing is saved");
        return resp;
    }

    [Test]
    public async Task UpdateManifest_UnparseableCronExpression_IsRefusedAndNotSaved()
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(
            id,
            new UpdateManifestInput(ScheduleType: ScheduleType.Cron, CronExpression: "every day")
        );

        resp.Message.Should().Contain("5 or 6");
    }

    [Test]
    public async Task UpdateManifest_SwitchToCronWithoutAnExpression_IsRefused()
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(
            id,
            new UpdateManifestInput(ScheduleType: ScheduleType.Cron)
        );

        resp.Message.Should().Contain("cronExpression");
    }

    [Test]
    public async Task UpdateManifest_BlankCronExpression_IsRefused()
    {
        var id = await SeedIntervalManifest();

        await RefusedUpdate(
            id,
            new UpdateManifestInput(ScheduleType: ScheduleType.Cron, CronExpression: "   ")
        );
    }

    [TestCase(0)]
    [TestCase(-60)]
    public async Task UpdateManifest_NonPositiveInterval_IsRefused(int seconds)
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(id, new UpdateManifestInput(IntervalSeconds: seconds));

        resp.Message.Should().Contain("intervalSeconds");
    }

    [Test]
    public async Task UpdateManifest_SwitchToIntervalWithoutAnInterval_IsRefused()
    {
        var id = await SeedIntervalManifest();
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync(x => x.Id == id);
            m.ScheduleType = ScheduleType.Cron;
            m.CronExpression = "0 0 * * *";
            m.IntervalSeconds = null;
            await db.SaveChanges(default);
        }

        var resp = await new OperationsMutations().UpdateManifest(
            id,
            new UpdateManifestInput(ScheduleType: ScheduleType.Interval),
            _factory,
            new RecordingChangeSignal(),
            default
        );

        resp.Success.Should().BeFalse();
        resp.Message.Should().Contain("intervalSeconds");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task UpdateManifest_ZeroTimeout_IsRefused(int seconds)
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(id, new UpdateManifestInput(TimeoutSeconds: seconds));

        resp.Message.Should().Contain("timeoutSeconds");
    }

    [Test]
    public async Task UpdateManifest_NegativeMaxRetries_IsRefused()
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(id, new UpdateManifestInput(MaxRetries: -1));

        resp.Message.Should().Contain("maxRetries");
    }

    [TestCase(ScheduleType.Once)]
    [TestCase(ScheduleType.Dependent)]
    [TestCase(ScheduleType.DormantDependent)]
    public async Task UpdateManifest_SwitchToATypeTheInputCannotComplete_IsRefused(
        ScheduleType type
    )
    {
        var id = await SeedIntervalManifest();

        var resp = await RefusedUpdate(id, new UpdateManifestInput(ScheduleType: type));

        resp.Message.Should().Contain(type.ToString());
    }

    [Test]
    public async Task UpdateManifest_SameTypeTheInputCannotComplete_IsKept()
    {
        await SeedManifests(1);
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync();
            m.ScheduleType = ScheduleType.Once;
            await db.SaveChanges(default);
            id = m.Id;
        }

        var resp = await new OperationsMutations().UpdateManifest(
            id,
            new UpdateManifestInput(ScheduleType: ScheduleType.Once, Priority: 3),
            _factory,
            new RecordingChangeSignal(),
            default
        );

        resp.Success.Should().BeTrue(resp.Message);
    }

    [Test]
    public async Task UpdateManifest_FieldsOutsideTheSchedule_SaveWhenTheStoredScheduleIsUnrunnable()
    {
        // The schedule is checked only when the input changes it, so an operator can still
        // disable a manifest whose stored schedule the scheduler cannot run.
        await SeedManifests(1);
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync();
            m.ScheduleType = ScheduleType.Cron;
            m.CronExpression = null;
            await db.SaveChanges(default);
            id = m.Id;
        }

        var resp = await new OperationsMutations().UpdateManifest(
            id,
            new UpdateManifestInput(IsEnabled: false),
            _factory,
            new RecordingChangeSignal(),
            default
        );

        resp.Success.Should().BeTrue(resp.Message);
    }

    [Test]
    public async Task UpdateManifest_MissingId_ReturnsFalse()
    {
        var signal = new RecordingChangeSignal();
        var resp = await new OperationsMutations().UpdateManifest(
            999999,
            new UpdateManifestInput(IsEnabled: true),
            _factory,
            signal,
            default
        );

        resp.Success.Should().BeFalse();
        signal.Domains.Should().BeEmpty("a missing manifest makes no change to signal");
    }

    [Test]
    public async Task UpdateManifest_SetsTimeoutAndInterval_ThenClearsTimeout()
    {
        await SeedManifests(1);
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
            id = await db.Manifests.Select(m => m.Id).FirstAsync();

        // First patch exercises the TimeoutSeconds (else-if) and IntervalSeconds branches.
        var mutations = new OperationsMutations();
        var signal = new RecordingChangeSignal();
        var set = await mutations.UpdateManifest(
            id,
            new UpdateManifestInput(
                ScheduleType: ScheduleType.Interval,
                IntervalSeconds: 60,
                TimeoutSeconds: 120
            ),
            _factory,
            signal,
            default
        );
        set.Success.Should().BeTrue();
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync(x => x.Id == id);
            m.ScheduleType.Should().Be(ScheduleType.Interval);
            m.IntervalSeconds.Should().Be(60);
            m.TimeoutSeconds.Should().Be(120);
        }

        // Second patch exercises the ClearTimeout branch (takes precedence over TimeoutSeconds).
        var cleared = await mutations.UpdateManifest(
            id,
            new UpdateManifestInput(ClearTimeout: true, TimeoutSeconds: 999),
            _factory,
            signal,
            default
        );
        cleared.Success.Should().BeTrue();
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = await db.Manifests.FirstAsync(x => x.Id == id);
            m.TimeoutSeconds.Should().BeNull();
        }
    }

    private async Task<long> SeedRequeueSourceAsync(
        string? input,
        bool recordDecision = false,
        string? trainName = null,
        long? replayDecisionsOf = null
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName ?? typeof(IRequeueProbeTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
        meta.Input = input;
        await db.Track(meta);
        await db.SaveChanges(default);

        if (recordDecision)
        {
            db.RecordedDecisions.Add(
                new RecordedDecision
                {
                    MetadataId = meta.Id,
                    QuestionKey = "Route",
                    Occurrence = 0,
                    Fingerprint = new string('0', 64),
                    Kind = "choice",
                    Question = "{}",
                    Answer = "\"Express\"",
                    Routes = """[{"track": "Express", "fallback_reason": null}]""",
                    DecidedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChanges(default);
        }

        return meta.Id;
    }

    /// <summary>
    /// The real operations service over this fixture's database, with the probe train
    /// registered and the mediator substituted, so a test sees exactly what reached the enqueue.
    /// </summary>
    private IOperationsService RequeueOperations(out ITrainExecutionService execution)
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IRequeueProbeTrain),
                    ImplementationType = typeof(IRequeueProbeTrain),
                    InputType = typeof(object),
                    OutputType = typeof(object),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = typeof(IRequeueProbeTrain).FullName!,
                    ImplementationTypeName = typeof(IRequeueProbeTrain).FullName!,
                    InputTypeName = typeof(object).FullName!,
                    OutputTypeName = typeof(object).FullName!,
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = true,
                    IsBroadcastEnabled = false,
                    IsRemote = false,
                    GraphQLOperations = GraphQLOperation.Queue,
                },
            ]);

        execution = Substitute.For<ITrainExecutionService>();
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(11, "ext-11"));
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<QueueTrainOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(12, "ext-12"));

        return new OperationsService(discovery, _factory, new SchedulerConfiguration(), execution);
    }

    [Test]
    public async Task RequeueExecution_GoesThroughTheSharedRequeue()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.RequeueExecutionAsync(42, Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Id: 7, Count: 1, Message: "queued"));

        var resp = await new OperationsMutations().RequeueExecution(42, ops, default);

        resp.Success.Should().BeTrue();
        resp.Id.Should().Be(7);
        resp.Message.Should().Be("queued");
        await ops.Received(1).RequeueExecutionAsync(42, Arg.Any<CancellationToken>());
        await ops.DidNotReceive()
            .QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequeueExecution_ARunThatRecordedDecisions_IsQueuedToReplayThem()
    {
        var id = await SeedRequeueSourceAsync("{\"v\": 1}", recordDecision: true);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeTrue(resp.Message);
        resp.Id.Should().Be(12);
        await execution
            .Received(1)
            .QueueAsync(
                typeof(IRequeueProbeTrain).FullName!,
                "{\"v\": 1}",
                Arg.Is<QueueTrainOptions>(o => o!.ReplayDecisionsOf == id),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task RequeueExecution_AskedAfresh_IsQueuedWithoutReplayingTheDecisions()
    {
        var id = await SeedRequeueSourceAsync("{\"v\": 1}", recordDecision: true);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(
            id,
            ops,
            default,
            askAfresh: true
        );

        resp.Success.Should().BeTrue(resp.Message);
        resp.Count.Should().Be(1);
        await execution
            .DidNotReceive()
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Is<QueueTrainOptions>(o => o != null && o.ReplayDecisionsOf != null),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RequeueExecution_PassesAskAfreshToTheSharedRequeue(bool askAfresh)
    {
        var ops = Substitute.For<IOperationsService>();
        ops.RequeueExecutionAsync(42, Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Count: 1));
        ops.RequeueExecutionAsync(42, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Count: 1));

        await new OperationsMutations().RequeueExecution(42, ops, default, askAfresh);

        if (askAfresh)
            await ops.Received(1).RequeueExecutionAsync(42, true, Arg.Any<CancellationToken>());
        else
        {
            await ops.Received(1).RequeueExecutionAsync(42, Arg.Any<CancellationToken>());
            await ops.DidNotReceive()
                .RequeueExecutionAsync(
                    Arg.Any<long>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>()
                );
        }
    }

    [Test]
    public async Task RequeueExecution_ARunThatRecordedNoDecisions_IsQueuedAsAnOrdinaryEnqueue()
    {
        var id = await SeedRequeueSourceAsync("{\"v\": 1}");
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeTrue(resp.Message);
        resp.Id.Should().Be(11);
        await execution
            .Received(1)
            .QueueAsync(
                typeof(IRequeueProbeTrain).FullName!,
                "{\"v\": 1}",
                0,
                null,
                Arg.Any<CancellationToken>()
            );
        await execution
            .DidNotReceive()
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<QueueTrainOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task RequeueExecution_ARequeueThatRecordedNothing_IsQueuedToReplayWhatItReplayed()
    {
        var first = await SeedRequeueSourceAsync("{\"v\": 1}", recordDecision: true);
        var second = await SeedRequeueSourceAsync("{\"v\": 1}", replayDecisionsOf: first);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(second, ops, default);

        resp.Success.Should().BeTrue(resp.Message);
        await execution
            .Received(1)
            .QueueAsync(
                typeof(IRequeueProbeTrain).FullName!,
                "{\"v\": 1}",
                Arg.Is<QueueTrainOptions>(o => o!.ReplayDecisionsOf == second),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task RequeueExecution_ARunWhoseTrainIsNoLongerRegistered_SaysSo()
    {
        var id = await SeedRequeueSourceAsync("{\"v\": 1}", trainName: "Some.Retired.ITrain");
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should()
            .Be(
                $"Train Some.Retired.ITrain is no longer registered, so execution {id} cannot be "
                    + "re-queued."
            );
        execution.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task RequeueExecution_ASavedInputThatNoLongerReads_IsRefusedAsTheRunsInput()
    {
        var id = await SeedRequeueSourceAsync("{\"v\": 1}");
        var ops = RequeueOperations(out var execution);
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<QueueTrainResult>(_ => throw new JsonException("v is not a member"));

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should()
            .Be($"The saved input of run {id} no longer reads as System.Object: v is not a member");
    }

    [Test]
    public async Task RequeueExecution_WithNoSavedInput_ReturnsFalseWithoutQueuing()
    {
        var id = await SeedRequeueSourceAsync(null);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should().Contain("no saved input");
        execution.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task RequeueExecution_WithATruncatedSavedInput_ReturnsFalseWithoutQueuing()
    {
        // What an input over MaxParameterBytes is saved as.
        var id = await SeedRequeueSourceAsync("{\"_truncated\": true, \"_maxBytes\": 1024}");
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should().Contain("too large to save");
        execution.ReceivedCalls().Should().BeEmpty();
    }

    [TestCase("{\"_unserializable\": true, \"_error\": \"NotSupportedException\"}")]
    [TestCase(
        "{\"_disposed\": true, \"_message\": \"Input object contained disposed JsonDocument objects\"}"
    )]
    [TestCase("{\"_truncated\": true, \"_maxBytes\": 1024}")]
    public async Task RequeueExecution_WithASavedPlaceholder_ReturnsFalseWithoutQueuing(
        string placeholder
    )
    {
        // What the parameter effect saves in place of an input it could not record.
        var id = await SeedRequeueSourceAsync(placeholder);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeFalse();
        execution.ReceivedCalls().Should().BeEmpty();
    }

    [TestCase("{\"_unserializable\": false, \"v\": 1}")]
    [TestCase("{\"_disposed\": \"yes\"}")]
    [TestCase("[1, 2]")]
    public async Task RequeueExecution_WithAnInputThatIsNotAPlaceholder_IsQueued(string input)
    {
        var id = await SeedRequeueSourceAsync(input);
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(id, ops, default);

        resp.Success.Should().BeTrue(resp.Message);
        execution.ReceivedCalls().Should().NotBeEmpty();
    }

    [Test]
    public async Task RequeueExecution_MissingId_ReturnsFalseWithoutQueuing()
    {
        var ops = RequeueOperations(out var execution);

        var resp = await new OperationsMutations().RequeueExecution(999999, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should().Be("Execution 999999 not found.");
        execution.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task GetGroups_PaginatesAndExposesCursor()
    {
        await SeedManifestGroup("g1");
        await SeedManifestGroup("g2");
        await SeedManifestGroup("g3");
        var queries = new ManifestGroupQueries();

        var result = await queries.GetGroups(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetGroups_AfterIdCursor_FiltersAndUsesExactCount()
    {
        await SeedManifestGroup("g1");
        await SeedManifestGroup("g2");
        await SeedManifestGroup("g3");
        var queries = new ManifestGroupQueries();
        var first = await queries.GetGroups(_factory, default, take: 1);

        var page2 = await queries.GetGroups(_factory, default, take: 2, afterId: first.NextCursor);

        page2.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetGroup_ReturnsSingleRecord_WhenIdMatches()
    {
        var seededId = await SeedManifestGroup("only");
        var queries = new ManifestGroupQueries();

        var fetched = await queries.GetGroup(seededId, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("only");
    }

    [Test]
    public async Task GetGroup_ReturnsNull_WhenIdDoesNotExist()
    {
        var queries = new ManifestGroupQueries();

        var fetched = await queries.GetGroup(999_999, _factory, default);

        fetched.Should().BeNull();
    }

    [Test]
    public async Task GetExecutions_PaginatesAndExposesCursor()
    {
        await SeedExecutions(3);
        var queries = new OperationsQueries();

        var result = await queries.GetExecutions(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetExecutions_AfterIdCursor_FiltersByCursor()
    {
        await SeedExecutions(4);
        var queries = new OperationsQueries();
        var first = await queries.GetExecutions(_factory, default, take: 1);

        var page2 = await queries.GetExecutions(
            _factory,
            default,
            take: 5,
            afterId: first.NextCursor
        );

        page2
            .Items.Select(e => e.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
    }

    [Test]
    public async Task GetExecutions_SkipPagination_Honors()
    {
        await SeedExecutions(5);
        var queries = new OperationsQueries();

        var result = await queries.GetExecutions(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetExecution_ById_ReturnsRow()
    {
        await SeedExecutions(1);
        var queries = new OperationsQueries();
        var first = (await queries.GetExecutions(_factory, default)).Items.First();

        var fetched = await queries.GetExecution(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetExecution_MissingId_ReturnsNull()
    {
        var queries = new OperationsQueries();

        (await queries.GetExecution(99999, _factory, default)).Should().BeNull();
    }

    [Test]
    public void GetTrains_NoTrainsRegistered_ReturnsEmpty()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        NSubstitute.SubstituteExtensions.Returns(
            discovery.DiscoverTrains(),
            new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>()
        );
        var queries = new OperationsQueries();

        var result = queries.GetTrains(discovery);

        result.Should().BeEmpty();
    }

    [Test]
    public void GetTrains_HideAdminTrains_FiltersOutFrameworkTrains()
    {
        // The IManifestManagerTrain is a framework admin train listed in
        // AdminTrains.FullNames. With hideAdminTrains=true it should not appear.
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registrations = new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>
        {
            FakeRegistration(typeof(Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain)),
            FakeRegistration(typeof(IUserTrain)),
        };
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), registrations);
        var queries = new OperationsQueries();

        var unfiltered = queries.GetTrains(discovery);
        var filtered = queries.GetTrains(discovery, hideAdminTrains: true);

        unfiltered.Should().HaveCount(2);
        filtered.Should().HaveCount(1);
        filtered.Single().ServiceTypeName.Should().NotContain("ManifestManager");
    }

    [Test]
    public void GetTrains_HideAdminTrainsFalse_ReturnsAll()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registrations = new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>
        {
            FakeRegistration(typeof(Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain)),
            FakeRegistration(typeof(IUserTrain)),
        };
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), registrations);
        var queries = new OperationsQueries();

        queries.GetTrains(discovery, hideAdminTrains: false).Should().HaveCount(2);
    }

    private static Trax.Mediator.Services.TrainDiscovery.TrainRegistration FakeRegistration(
        Type serviceType,
        Type? inputType = null
    )
    {
        return new Trax.Mediator.Services.TrainDiscovery.TrainRegistration
        {
            ServiceType = serviceType,
            ImplementationType = serviceType,
            InputType = inputType ?? typeof(FakeInput),
            OutputType = typeof(FakeOutput),
            Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
            ServiceTypeName = serviceType.Name,
            ImplementationTypeName = serviceType.Name,
            InputTypeName = typeof(FakeInput).FullName!,
            OutputTypeName = typeof(FakeOutput).FullName!,
            RequiredPolicies = Array.Empty<string>(),
            RequiredRoles = Array.Empty<string>(),
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations =
                Trax.Effect.Attributes.GraphQLOperation.Run
                | Trax.Effect.Attributes.GraphQLOperation.Queue,
        };
    }

    private interface IUserTrain
        : Trax.Effect.Services.ServiceTrain.IServiceTrain<FakeInput, FakeOutput> { }

    [Test]
    public void DeadLettersNamespace_ReturnsNewInstance()
    {
        var queries = new OperationsQueries();

        var ns = queries.DeadLetters();

        ns.Should().NotBeNull();
    }

    [Test]
    public async Task GetDeadLetters_NoData_ReturnsEmpty()
    {
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(_factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Test]
    public async Task GetDeadLetters_FiltersByStatus()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(3, manifest);
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(
            _factory,
            default,
            status: DeadLetterStatus.AwaitingIntervention
        );

        result.Items.Should().HaveCount(3);
    }

    [Test]
    public async Task GetDeadLetters_PaginatesWithCursor()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(4, manifest);
        var queries = new DeadLetterQueries();
        var first = await queries.GetDeadLetters(_factory, default, take: 2);

        var page2 = await queries.GetDeadLetters(
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(dl => dl.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
    }

    [Test]
    public async Task GetDeadLetters_CursorPage_TotalCountIsEveryMatchingRecord()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(5, manifest);
        var queries = new DeadLetterQueries();
        var first = await queries.GetDeadLetters(_factory, default, take: 2);

        var page2 = await queries.GetDeadLetters(
            _factory,
            default,
            skip: 3,
            take: 2,
            afterId: first.NextCursor
        );

        // Every other paged operations read counts the filter, not the rows after the cursor,
        // and reports skip as 0 once a cursor is given ("skip is ignored").
        first.TotalCount.Should().Be(5);
        page2.TotalCount.Should().Be(5, "totalCount is the number of records matching the query");
        page2.Skip.Should().Be(0, "skip is ignored when afterId is supplied");
    }

    [Test]
    public async Task GetDeadLetters_SkipHonored()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(5, manifest);
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetDeadLetter_ById_ReturnsRow()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(1, manifest);
        var queries = new DeadLetterQueries();
        var first = (await queries.GetDeadLetters(_factory, default)).Items.First();

        var fetched = await queries.GetDeadLetter(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetDeadLetter_MissingId_ReturnsNull()
    {
        var queries = new DeadLetterQueries();

        (await queries.GetDeadLetter(99999, _factory, default)).Should().BeNull();
    }

    #region Read fields an API-only frontend needs

    private static readonly IServiceProvider EmptyServices = Substitute.For<IServiceProvider>();

    [Test]
    public async Task GetExecutionDetail_CarriesParentScheduleExecutorAndHostLabels()
    {
        long parentId;
        long childId;
        var due = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
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
            parentId = parent.Id;

            var child = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.Child",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ParentId = parentId,
                }
            );
            child.ScheduledTime = due;
            child.HostLabels = "{\"region\": \"eu-west-1\"}";
            await db.Track(child);
            await db.SaveChanges(default);
            childId = child.Id;
        }

        string? executor;
        await using (var db = await _factory.CreateDbContextAsync(default))
            executor = await db
                .Metadatas.AsNoTracking()
                .Where(m => m.Id == childId)
                .Select(m => m.Executor)
                .SingleAsync();

        var detail = await new OperationsQueries().GetExecutionDetail(childId, _factory, default);

        detail.Should().NotBeNull();
        detail!.ParentId.Should().Be(parentId);
        detail.ScheduledTime.Should().Be(due);
        detail.Executor.Should().Be(executor);
        detail.HostLabels.Should().Be("{\"region\": \"eu-west-1\"}");
    }

    [Test]
    public async Task GetExecutionDetail_CarriesTheRunARequeueReplays()
    {
        var source = await SeedRequeueSourceAsync("{\"v\": 1}", recordDecision: true);
        var requeued = await SeedRequeueSourceAsync("{\"v\": 1}", replayDecisionsOf: source);

        var queries = new OperationsQueries();
        var replaying = await queries.GetExecutionDetail(requeued, _factory, default);
        var original = await queries.GetExecutionDetail(source, _factory, default);

        replaying!
            .ReplayDecisionsOf.Should()
            .Be(source, "the dashboard's Replays Decisions Of reads the same column");
        original!.ReplayDecisionsOf.Should().BeNull();
    }

    [Test]
    public async Task GetManifests_AndGetManifest_CarryTheGroupName()
    {
        var groupId = await SeedManifestGroup("nightly-billing");
        var manifestId = await SeedManifestInGroup(groupId);

        var queries = new OperationsQueries();
        var page = await queries.GetManifests(_factory, default);
        var single = await queries.GetManifest(manifestId, _factory, default);

        page.Items.Should().ContainSingle().Which.ManifestGroupName.Should().Be("nightly-billing");
        single!.ManifestGroupName.Should().Be("nightly-billing");
    }

    [Test]
    public async Task GetManifestDetail_CarriesPropertiesAndScheduling()
    {
        var groupId = await SeedManifestGroup("detail-group");
        long id;
        var next = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var m = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(SomeFakeTrain),
                    MisfirePolicy = MisfirePolicy.DoNothing,
                    MisfireThresholdSeconds = 90,
                    VarianceSeconds = 30,
                }
            );
            m.ManifestGroupId = groupId;
            m.PropertyTypeName = "Trax.X.BillingInput";
            m.Properties = "{\"accountId\": \"acct-1\"}";
            m.NextScheduledRun = next;
            await db.Track(m);
            await db.SaveChanges(default);
            id = m.Id;
        }

        var detail = await new OperationsQueries().GetManifestDetail(
            id,
            _factory,
            Discovery,
            default
        );

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(id);
        detail.ManifestGroupId.Should().Be(groupId);
        detail.ManifestGroupName.Should().Be("detail-group");
        detail.PropertyTypeName.Should().Be("Trax.X.BillingInput");
        // No train on this host takes Trax.X.BillingInput, so nothing shows the stored properties
        // hold no [TraxSensitive] member: the read masks them whole.
        detail.Properties.Should().Contain("_redacted").And.NotContain("acct-1");
        detail.MisfirePolicy.Should().Be(MisfirePolicy.DoNothing);
        detail.MisfireThresholdSeconds.Should().Be(90);
        detail.VarianceSeconds.Should().Be(30);
        detail.NextScheduledRun.Should().Be(next);
    }

    [Test]
    public async Task ManifestReads_CarryWhetherARetryReplaysDecisions()
    {
        var groupId = await SeedManifestGroup("replay-group");
        long optedOut;
        long byDefault;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var off = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
            off.ManifestGroupId = groupId;
            off.ReplayDecisionsOnRetry = false;
            var on = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
            on.ManifestGroupId = groupId;
            await db.Track(off);
            await db.Track(on);
            await db.SaveChanges(default);
            optedOut = off.Id;
            byDefault = on.Id;
        }

        var queries = new OperationsQueries();

        (await queries.GetManifest(optedOut, _factory, default))!
            .ReplayDecisionsOnRetry.Should()
            .BeFalse();
        (await queries.GetManifest(byDefault, _factory, default))!
            .ReplayDecisionsOnRetry.Should()
            .BeTrue();
        (await queries.GetManifestDetail(optedOut, _factory, Discovery, default))!
            .ReplayDecisionsOnRetry.Should()
            .BeFalse();
        (await queries.GetManifests(_factory, default, manifestGroupId: groupId))
            .Items.Should()
            .Contain(m => m.Id == optedOut && !m.ReplayDecisionsOnRetry)
            .And.Contain(m => m.Id == byDefault && m.ReplayDecisionsOnRetry);
    }

    [Test]
    public async Task GetManifestDetail_MissingId_ReturnsNull()
    {
        (await new OperationsQueries().GetManifestDetail(99999, _factory, Discovery, default))
            .Should()
            .BeNull();
    }

    [Test]
    public void GetEffects_ConfigurableFactory_CarriesItsSettings()
    {
        var registry = Substitute.For<IEffectRegistry>();
        registry
            .GetAll()
            .Returns(
                new Dictionary<Type, bool>
                {
                    { typeof(FakeConfigurableFactory), true },
                    { typeof(FakeInput), true },
                }
            );
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(FakeConfigurableFactory)).Returns(new FakeConfigurableFactory());

        var result = new OperationsQueries().GetEffects(registry, services);

        var configurable = result.Single(e => e.Name == nameof(FakeConfigurableFactory));
        configurable.IsConfigurable.Should().BeTrue();
        configurable.ConfigurationTypeName.Should().Be(typeof(FakeEffectSettings).FullName);
        configurable.Configuration.Should().Contain("\"batchSize\"").And.Contain("50");
        var plain = result.Single(e => e.Name == nameof(FakeInput));
        plain.IsConfigurable.Should().BeFalse();
        plain.ConfigurationTypeName.Should().BeNull();
        plain.Configuration.Should().BeNull();
    }

    [Test]
    public void GetEffects_SettingsThatCannotBeSerialized_ReadAsNullWithoutFailingTheList()
    {
        var registry = Substitute.For<IEffectRegistry>();
        registry
            .GetAll()
            .Returns(new Dictionary<Type, bool> { { typeof(FakeUnwritableFactory), true } });
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(FakeUnwritableFactory)).Returns(new FakeUnwritableFactory());

        var effect = new OperationsQueries().GetEffects(registry, services).Single();

        effect.IsConfigurable.Should().BeTrue();
        effect.Configuration.Should().BeNull();
    }

    [Test]
    public void GetEffects_SensitiveSettingMembers_ReadMasked()
    {
        var registry = Substitute.For<IEffectRegistry>();
        registry
            .GetAll()
            .Returns(new Dictionary<Type, bool> { { typeof(FakeCredentialedFactory), true } });
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(FakeCredentialedFactory)).Returns(new FakeCredentialedFactory());

        var configuration = new OperationsQueries()
            .GetEffects(registry, services)
            .Single()
            .Configuration;

        using var json = System.Text.Json.JsonDocument.Parse(configuration!);
        var root = json.RootElement;
        root.GetProperty("endpoint").GetString().Should().Be("https://sink.example");
        root.GetProperty("apiKey").GetProperty("_redacted").GetBoolean().Should().BeTrue();
        root.GetProperty("nested")
            .GetProperty("password")
            .GetProperty("_redacted")
            .GetBoolean()
            .Should()
            .BeTrue();
        configuration.Should().NotContain("sk-live-123").And.NotContain("hunter2");
    }

    public record FakeNestedCredential(
        [property: Trax.Effect.Attributes.TraxSensitive] string Password
    );

    public record FakeCredentialedSettings(
        string Endpoint,
        [property: Trax.Effect.Attributes.TraxSensitive] string ApiKey,
        object Nested
    );

    public class FakeCredentialedFactory
        : Trax.Effect.Services.EffectProviderFactory.IConfigurableProviderFactory
    {
        public object GetConfiguration() =>
            new FakeCredentialedSettings(
                "https://sink.example",
                "sk-live-123",
                new FakeNestedCredential("hunter2")
            );

        public Type GetConfigurationType() => typeof(FakeCredentialedSettings);
    }

    public record FakeUnwritableSettings(Action Callback);

    public class FakeUnwritableFactory
        : Trax.Effect.Services.EffectProviderFactory.IConfigurableProviderFactory
    {
        public object GetConfiguration() => new FakeUnwritableSettings(() => { });

        public Type GetConfigurationType() => typeof(FakeUnwritableSettings);
    }

    [Test]
    public void GetTrains_InputSchema_UsesTheSystemNamingPolicyAndListsEnumValues()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registration = FakeRegistration(typeof(IUserTrain), typeof(SchemaInput));
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), [registration]);

        var schema = new OperationsQueries().GetTrains(discovery).Single().InputSchema;

        schema
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(["playerId", "tier", "maybeTier", "custom_name"]);
        schema.Single(p => p.Name == "tier").EnumValues.Should().Equal("Bronze", "Silver", "Gold");
        schema
            .Single(p => p.Name == "maybeTier")
            .EnumValues.Should()
            .Equal("Bronze", "Silver", "Gold");
        schema.Single(p => p.Name == "playerId").EnumValues.Should().BeNull();
    }

    [Test]
    public void GetTrains_InputSchema_ListsConditionallyIgnoredPropertiesTheReaderAccepts()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registration = FakeRegistration(typeof(IUserTrain), typeof(IgnoreConditionInput));
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), [registration]);

        var schema = new OperationsQueries().GetTrains(discovery).Single().InputSchema;

        schema
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(
                ["playerId", "nickname", "bonus"],
                "only Condition = Always hides a property from the reader"
            );
    }

    [Test]
    public void GetTrains_InputSchema_LeavesOutAPropertyItCannotRead()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registration = FakeRegistration(typeof(IUserTrain), typeof(WriteOnlyInput));
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), [registration]);

        var schema = new OperationsQueries().GetTrains(discovery).Single().InputSchema;

        schema.Select(p => p.Name).Should().Equal("playerId");
    }

    public class WriteOnlyInput
    {
        public string PlayerId { get; set; } = "";

        public string Secret
        {
            set => _ = value;
        }
    }

    [Test]
    public async Task GetGroups_NameFilter_CountsExactly()
    {
        await SeedManifestGroup("billing-a");
        await SeedManifestGroup("billing-b");
        await SeedManifestGroup("search");

        var result = await new ManifestGroupQueries().GetGroups(
            _factory,
            default,
            nameContains: "billing"
        );

        result.Items.Select(g => g.Name).Should().BeEquivalentTo(["billing-a", "billing-b"]);
        result.TotalCount.Should().Be(2);
        result.IsEstimatedCount.Should().BeFalse("a filtered count is exact");
    }

    [Test]
    public async Task CancelExecution_ARefusedRequest_IsReturnedAsTheRefusal()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.CancelExecutionsAsync(
                Arg.Any<IReadOnlyCollection<long>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(false, Count: 0, Message: "refused"));

        var resp = await new OperationsMutations().CancelExecution(7, ops, default);

        resp.Success.Should().BeFalse();
        resp.Message.Should().Be("refused");
    }

    public record IgnoreConditionInput(
        string PlayerId,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        )]
            string? Nickname,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        )]
            int Bonus,
        [property: System.Text.Json.Serialization.JsonIgnore] string? Internal
    );

    public enum SchemaTier
    {
        Bronze,
        Silver,
        Gold,
    }

    public record SchemaInput(
        string PlayerId,
        SchemaTier Tier,
        SchemaTier? MaybeTier,
        [property: System.Text.Json.Serialization.JsonPropertyName("custom_name")] string Renamed
    );

    public record FakeEffectSettings(int BatchSize, string Target);

    public class FakeConfigurableFactory
        : Trax.Effect.Services.EffectProviderFactory.IConfigurableProviderFactory
    {
        public object GetConfiguration() => new FakeEffectSettings(50, "sink");

        public Type GetConfigurationType() => typeof(FakeEffectSettings);
    }

    #endregion

    public interface IRequeueProbeTrain;

    private interface ISomeFakeTrain
        : Trax.Effect.Services.ServiceTrain.IServiceTrain<FakeInput, FakeOutput> { }

    private class SomeFakeTrain { }

    public record FakeInput;

    public record FakeOutput;
}
