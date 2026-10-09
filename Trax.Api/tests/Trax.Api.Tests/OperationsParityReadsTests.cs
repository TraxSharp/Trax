using AwesomeAssertions;
using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.Runs;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Trax.Api.Tests;

/// <summary>
/// The reads the dashboard has and the operations surface gained to match it: the executions,
/// manifests, work queue, dead letter and log filters, the fields the run and queue grids show,
/// and a run's recorded decisions. Runs against Postgres, the provider the indexes these filters
/// seek are declared on.
/// </summary>
[TestFixture]
public class OperationsParityReadsTests
{
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=30;"
        + "Timeout=30;Tcp Keepalive=true";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

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
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.log, trax.dead_letter, trax.work_queue, trax.metadata, "
                + "trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    #region Executions

    [Test]
    public async Task Executions_FilterByExternalId_ReturnsThatRunOnly()
    {
        await SeedRun();
        var wanted = await SeedRun();
        await SeedRun();
        var externalId = await ExternalIdOf(wanted);

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            externalId: externalId
        );

        page.Items.Should().ContainSingle().Which.Id.Should().Be(wanted);
        page.TotalCount.Should().Be(1);
        page.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task Executions_FilterByParentId_ListsTheChildren_AndEachCarriesItsParent()
    {
        var parent = await SeedRun();
        var first = await SeedRun(m => m.ParentId = parent);
        var second = await SeedRun(m => m.ParentId = parent);
        await SeedRun();

        var page = await new OperationsQueries().GetExecutions(_factory, default, parentId: parent);

        page.Items.Select(e => e.Id).Should().Equal(second, first);
        page.Items.Should().OnlyContain(e => e.ParentId == parent);
        page.TotalCount.Should().Be(2);
    }

    [Test]
    public async Task Executions_FilterByHostName_MatchesExactly()
    {
        var onA = await SeedRun(m => m.HostName = "host-a");
        await SeedRun(m => m.HostName = "host-ab");
        await SeedRun(m => m.HostName = null);

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            hostName: "host-a"
        );

        page.Items.Should().ContainSingle().Which.Id.Should().Be(onA);
    }

    [Test]
    public async Task Executions_FailureReasonContains_IgnoresCase_AndCountsOnlyMatches()
    {
        var first = await SeedFailedRun("Card DECLINED by the issuer");
        await SeedFailedRun("Timed out waiting for the warehouse");
        var second = await SeedFailedRun("card declined: insufficient funds");
        await SeedRun();

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            failureReasonContains: "CARD declined"
        );

        page.Items.Select(e => e.Id).Should().Equal(second, first);
        page.TotalCount.Should().Be(2);
        page.IsCountCapped.Should().BeFalse();
        page.IsEstimatedCount.Should().BeFalse();
    }

    [TestCase("100%", "100% of the quota used", "100 of the quota used")]
    [TestCase("tenant_limit", "over tenant_limit", "over tenantXlimit")]
    [TestCase(@"c:\temp", @"cannot write C:\temp", "cannot write C:temp")]
    [TestCase("%", "50% done", "half done")]
    public async Task Executions_FailureReasonContains_AWildcardInTheTerm_MatchesItself(
        string term,
        string matching,
        string notMatching
    )
    {
        var wanted = await SeedFailedRun(matching);
        await SeedFailedRun(notMatching);

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            failureReasonContains: term
        );

        page.Items.Should().ContainSingle().Which.Id.Should().Be(wanted);
        page.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task Executions_FailureReasonContains_CombinesWithTheOtherFilters()
    {
        var wanted = await SeedFailedRun("connection refused", "ChargeCard", "host-a");
        await SeedFailedRun("connection refused", "ChargeCard", "host-b");
        await SeedFailedRun("connection refused", "ReserveStock", "host-a");
        await SeedFailedRun("permission denied", "ChargeCard", "host-a");

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            trainState: TrainState.Failed,
            hostName: "host-a",
            failureReasonContains: "refused",
            failureJunction: "ChargeCard"
        );

        page.Items.Should().ContainSingle().Which.Id.Should().Be(wanted);
        page.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task Executions_FailureJunction_MatchesExactly()
    {
        var wanted = await SeedFailedRun("failed", "ChargeCard");
        await SeedFailedRun("failed", "ChargeCardRetry");
        await SeedFailedRun("failed", "chargecard");
        await SeedRun();

        var page = await new OperationsQueries().GetExecutions(
            _factory,
            default,
            failureJunction: "ChargeCard"
        );

        page.Items.Should().ContainSingle().Which.Id.Should().Be(wanted);
        page.TotalCount.Should().Be(1);
    }

    [TestCase(SortOrder.Newest)]
    [TestCase(SortOrder.Oldest)]
    public async Task Executions_FailureReasonContains_ReadsMatchesOnBothSidesOfTheWindowInOrder(
        SortOrder order
    )
    {
        // More runs than the window, with matches near each end and in the middle, so a page
        // starts in the window and finishes past it, whichever end it reads from.
        var rows = OperationsQueries.FailureTextWindow + 20_000;
        await using (var db = await _factory.CreateDbContextAsync(default))
            await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO trax.metadata (external_id, name, train_state, start_time, failure_reason)
                SELECT lpad(g::text, 32, '0'), 'Trax.X.Parity', 'failed'::trax.train_state, now(),
                       'attempt ' || g || CASE WHEN g % 1000 = 7 OR g IN (3, {rows
                    - 2}) THEN ' Needle' ELSE '' END
                FROM generate_series(1, {rows}) g
                """
            );
        List<long> matching;
        await using (var db = await _factory.CreateDbContextAsync(default))
            matching = await db
                .Metadatas.Where(m => m.FailureReason!.EndsWith(" Needle"))
                .Select(m => m.Id)
                .ToListAsync();
        var expected =
            order == SortOrder.Oldest
                ? matching.Order().ToList()
                : matching.OrderDescending().ToList();
        var queries = new OperationsQueries();

        var whole = await queries.GetExecutions(
            _factory,
            default,
            take: 500,
            order: order,
            failureReasonContains: "needle"
        );
        whole.Items.Select(e => e.Id).Should().Equal(expected);
        whole.TotalCount.Should().Be(expected.Count);

        var paged = new List<long>();
        long? cursor = null;
        do
        {
            var page = await queries.GetExecutions(
                _factory,
                default,
                take: 7,
                order: order,
                afterId: cursor,
                failureReasonContains: "needle"
            );
            paged.AddRange(page.Items.Select(e => e.Id));
            page.TotalCount.Should().Be(expected.Count, "the cursor never changes the total");
            cursor = page.Items.Count == 7 ? page.NextCursor : null;
        } while (cursor is not null);

        paged.Should().Equal(expected, "paging by cursor reads the same rows in the same order");

        var bySkip = await queries.GetExecutions(
            _factory,
            default,
            skip: 7,
            take: 7,
            order: order,
            failureReasonContains: "needle"
        );
        bySkip
            .Items.Select(e => e.Id)
            .Should()
            .Equal(expected.Skip(7).Take(7), "an offset page reads the same order");
    }

    [Test]
    public async Task Executions_AFailureTextFilter_CountsUpToTheCap_AndSaysWhenItStopped()
    {
        await using (var db = await _factory.CreateDbContextAsync(default))
            await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO trax.metadata (external_id, name, train_state, start_time, failure_reason, failure_junction)
                SELECT lpad(g::text, 32, '0'), 'Trax.X.Parity', 'failed'::trax.train_state, now(),
                       'retrying ' || g, 'Retry'
                FROM generate_series(1, {OperationsQueries.FailureTextCountCap + 5}) g
                """
            );
        var queries = new OperationsQueries();

        var capped = await queries.GetExecutions(
            _factory,
            default,
            failureReasonContains: "retrying"
        );
        var exact = await queries.GetExecutions(
            _factory,
            default,
            failureReasonContains: "retrying 1000"
        );
        var byJunction = await queries.GetExecutions(_factory, default, failureJunction: "Retry");

        capped.TotalCount.Should().Be(OperationsQueries.FailureTextCountCap);
        capped.IsCountCapped.Should().BeTrue();
        capped
            .IsEstimatedCount.Should()
            .BeFalse("a capped count is a lower bound, not an estimate");
        capped.Items.Should().HaveCount(25);
        exact.TotalCount.Should().Be(7, "retrying 1000 and retrying 10000 to 10005");
        exact.IsCountCapped.Should().BeFalse();
        byJunction
            .TotalCount.Should()
            .Be(OperationsQueries.FailureTextCountCap + 5, "only a text filter is capped");
        byJunction.IsCountCapped.Should().BeFalse();
    }

    [Test]
    public async Task ExecutionSummary_CarriesTheRunningJunction_OnEveryRead()
    {
        var running = await SeedRun(m =>
        {
            m.TrainState = TrainState.InProgress;
            m.CurrentlyRunningJunction = "ChargeCard";
        });
        var queries = new OperationsQueries();

        var listed = (await queries.GetExecutions(_factory, default)).Items.Single();
        var single = await queries.GetExecution(running, _factory, default);

        listed.CurrentlyRunningJunction.Should().Be("ChargeCard");
        listed.ParentId.Should().BeNull();
        single!.CurrentlyRunningJunction.Should().Be("ChargeCard");
    }

    [Test]
    public async Task ExecutionChildren_CarryTheirParent()
    {
        var parent = await SeedRun();
        await SeedRun(m => m.ParentId = parent);

        var children = await new OperationsQueries().GetExecutionChildren(
            parent,
            _factory,
            default
        );

        children.Items.Should().ContainSingle().Which.ParentId.Should().Be(parent);
    }

    [Test]
    public async Task ExecutionDetail_CarriesWhetherTheReplayWasAbandoned()
    {
        var original = await SeedRun();
        var abandoned = await SeedRun(m =>
        {
            m.ReplayDecisionsOf = original;
            m.ReplayAbandoned = true;
        });
        var queries = new OperationsQueries();

        (await queries.GetExecutionDetail(abandoned, _factory, default))!
            .ReplayAbandoned.Should()
            .BeTrue();
        (await queries.GetExecutionDetail(original, _factory, default))!
            .ReplayAbandoned.Should()
            .BeFalse();
    }

    #endregion

    #region Manifests, dead letters, work queue

    [Test]
    public async Task Manifests_HideAdminTrains_LeavesOutTheSchedulersOwnManifests()
    {
        var group = await SeedGroup();
        var own = await SeedManifest(group);
        await SeedManifest(group, AdminTrains.FullNames[0]);
        var queries = new OperationsQueries();

        var hidden = await queries.GetManifests(_factory, default, hideAdminTrains: true);
        var all = await queries.GetManifests(_factory, default);

        hidden.Items.Should().ContainSingle().Which.Id.Should().Be(own);
        hidden.TotalCount.Should().Be(1);
        hidden.IsEstimatedCount.Should().BeFalse();
        all.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task DeadLetters_FilterByManifestId_ReturnsThatManifestsOnly()
    {
        var group = await SeedGroup();
        var wanted = await SeedManifest(group);
        var other = await SeedManifest(group);
        await SeedDeadLetter(wanted);
        await SeedDeadLetter(wanted);
        await SeedDeadLetter(other);

        var page = await new DeadLetterQueries().GetDeadLetters(
            _factory,
            default,
            manifestId: wanted
        );

        page.Items.Should().HaveCount(2).And.OnlyContain(d => d.ManifestId == wanted);
        page.TotalCount.Should().Be(2);
    }

    [Test]
    public async Task WorkQueues_FilterBySubjectKey_AndByManifestId()
    {
        var group = await SeedGroup();
        var manifest = await SeedManifest(group);
        var onSubject = await SeedEntry(
            new CreateWorkQueue { TrainName = "T", SubjectKey = "order-7" }
        );
        await SeedEntry(new CreateWorkQueue { TrainName = "T", SubjectKey = "order-70" });
        var ofManifest = await SeedEntry(
            new CreateWorkQueue { TrainName = "T", ManifestId = manifest }
        );
        var queries = new WorkQueueQueries();

        var bySubject = await queries.GetWorkQueues(_factory, default, subjectKey: "order-7");
        var byManifest = await queries.GetWorkQueues(_factory, default, manifestId: manifest);

        bySubject.Items.Should().ContainSingle().Which.Id.Should().Be(onSubject);
        bySubject.TotalCount.Should().Be(1);
        byManifest.Items.Should().ContainSingle().Which.Id.Should().Be(ofManifest);
    }

    [Test]
    public async Task WorkQueueEntries_CarryTheRunTheyReplay_OnTheListAndTheDetail()
    {
        var original = await SeedRun();
        var replaying = await SeedEntry(
            new CreateWorkQueue { TrainName = "T", ReplayDecisionsOf = original }
        );
        var queries = new WorkQueueQueries();

        (await queries.GetWorkQueues(_factory, default))
            .Items.Single()
            .ReplayDecisionsOf.Should()
            .Be(original);
        (await queries.GetWorkQueue(replaying, _factory, default))!
            .ReplayDecisionsOf.Should()
            .Be(original);
        (
            await queries.GetDetail(
                replaying,
                new OperationsService(
                    Substitute.For<ITrainDiscoveryService>(),
                    _factory,
                    new SchedulerConfiguration(),
                    Substitute.For<ITrainExecutionService>()
                ),
                default
            )
        )!
            .ReplayDecisionsOf.Should()
            .Be(original);
    }

    #endregion

    #region Logs

    [Test]
    public async Task Logs_MessageContains_IgnoresCase_AndCountsOnlyMatches()
    {
        var run = await SeedRun();
        await SeedLog(run, "Charged the card", "Shop.Payments");
        await SeedLog(run, "Shipped the parcel", "Shop.Shipping");
        await SeedLog(run, "Card declined", "Shop.Payments");

        var page = await new LogQueries().GetLogs(
            Operations,
            _factory,
            default,
            messageContains: "CARD"
        );

        page.Items.Select(l => l.Message).Should().Equal("Card declined", "Charged the card");
        page.TotalCount.Should().Be(2);
        page.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task Logs_CategoryContains_MatchesPartOfTheCategory()
    {
        var run = await SeedRun();
        await SeedLog(run, "a", "Shop.Payments");
        await SeedLog(run, "b", "Shop.Shipping");

        var page = await new LogQueries().GetLogs(
            Operations,
            _factory,
            default,
            categoryContains: "ship"
        );

        page.Items.Should().ContainSingle().Which.Category.Should().Be("Shop.Shipping");
    }

    [Test]
    public async Task Logs_OldestFirst_PagesForwardWithTheCursor()
    {
        var run = await SeedRun();
        for (var i = 0; i < 5; i++)
            await SeedLog(run, $"step {i}", "Shop");
        var queries = new LogQueries();

        var first = await queries.GetLogs(
            Operations,
            _factory,
            default,
            take: 2,
            metadataId: run,
            order: SortOrder.Oldest
        );
        var second = await queries.GetLogs(
            Operations,
            _factory,
            default,
            take: 2,
            metadataId: run,
            afterId: first.NextCursor,
            order: SortOrder.Oldest
        );

        first.Items.Select(l => l.Message).Should().Equal("step 0", "step 1");
        second.Items.Select(l => l.Message).Should().Equal("step 2", "step 3");
        second.TotalCount.Should().Be(5);
    }

    [Test]
    public async Task Logs_ATextFilter_CountsUpToTheCap_AndSaysWhenItStopped()
    {
        var run = await SeedRun();
        await using (var db = await _factory.CreateDbContextAsync(default))
            await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) SELECT {run}, 0, 'information'::trax.log_level, 'retrying ' || g, 'Shop' FROM generate_series(1, {OperationsService.LogCountCap + 5}) g"
            );
        var queries = new LogQueries();

        var capped = await queries.GetLogs(
            Operations,
            _factory,
            default,
            messageContains: "retrying"
        );
        var exact = await queries.GetLogs(
            Operations,
            _factory,
            default,
            messageContains: "retrying 1000"
        );
        var byRun = await queries.GetLogs(Operations, _factory, default, metadataId: run);

        capped.TotalCount.Should().Be(OperationsService.LogCountCap);
        capped.IsCountCapped.Should().BeTrue();
        capped
            .IsEstimatedCount.Should()
            .BeFalse("a capped count is a lower bound, not an estimate");
        exact.TotalCount.Should().Be(7, "retrying 1000 and retrying 10000 to 10005");
        exact.IsCountCapped.Should().BeFalse();
        byRun
            .TotalCount.Should()
            .Be(OperationsService.LogCountCap + 5, "only a text filter is capped");
        byRun.IsCountCapped.Should().BeFalse();
    }

    [Test]
    public async Task Logs_AWildcardInTheTerm_MatchesItself()
    {
        var run = await SeedRun();
        await SeedLog(run, "100% done", "Shop");
        await SeedLog(run, "100 done", "Shop");

        var page = await new LogQueries().GetLogs(
            Operations,
            _factory,
            default,
            messageContains: "100%"
        );

        page.Items.Should().ContainSingle().Which.Message.Should().Be("100% done");
    }

    #endregion

    #region Decisions

    [Test]
    public async Task Decisions_ReadsARunsDecisionsInOrder_AndPagesWithTheCursor()
    {
        var run = await SeedRun();
        var other = await SeedRun();
        for (var i = 0; i < 3; i++)
            await SeedDecision(run, "Route", i, "\"Express\"");
        await SeedDecision(other, "Route", 0, "\"Standard\"");
        var queries = new OperationsQueries();

        var first = await queries.GetDecisions(run, Operations, default, take: 2);
        var second = await queries.GetDecisions(
            run,
            Operations,
            default,
            afterId: first.NextCursor,
            take: 2
        );

        first.Items.Select(d => d.Occurrence).Should().Equal(0, 1);
        first.Take.Should().Be(2);
        second.Items.Should().ContainSingle().Which.Occurrence.Should().Be(2);
        first.Items.Concat(second.Items).Should().OnlyContain(d => d.MetadataId == run);
        first.Items[0].QuestionKey.Should().Be("Route");
        first.Items[0].Answer.Should().Be("\"Express\"");
        first.Items[0].Kind.Should().Be("choice");
    }

    [Test]
    public async Task Decisions_ReplayRefused_IsReadFromTheAnswer()
    {
        var run = await SeedRun();
        await SeedDecision(
            run,
            "Route",
            0,
            """{"value": "Express", "replay_refused": "the state changed"}"""
        );
        await SeedDecision(run, "Route", 1, "\"Standard\"");

        var page = await new OperationsQueries().GetDecisions(run, Operations, default);

        page.Items[0].ReplayRefused.Should().Be("the state changed");
        page.Items[1].ReplayRefused.Should().BeNull();
    }

    [Test]
    public async Task Decisions_ARunWithNone_IsEmpty()
    {
        var run = await SeedRun();

        var page = await new OperationsQueries().GetDecisions(run, Operations, default);

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task Decisions_ANonPositiveRunId_IsRefused()
    {
        var act = () => new OperationsQueries().GetDecisions(0, Operations, default);

        (await act.Should().ThrowAsync<GraphQLException>())
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be("TRAX_INVALID_ARGUMENT");
    }

    #endregion

    #region Run graph

    [Test]
    public async Task RunGraph_PlacesTheStepsJunctionRunsReads_OnTheTrainsGraph()
    {
        var run = await SeedRun();
        await SeedSteps(
            run,
            ("Fetch", JunctionRunKind.Junction, JunctionRunState.Completed, "Fetch#0", null),
            ("Lane", JunctionRunKind.Route, JunctionRunState.Completed, "Switch<Lane>#0", "Fast"),
            (
                "Ship",
                JunctionRunKind.Junction,
                JunctionRunState.Failed,
                "Switch<Lane>#0/Fast/Ship#0",
                null
            ),
            ("Legacy", JunctionRunKind.Junction, JunctionRunState.Completed, null, null)
        );
        var graphs = Substitute.For<ITrainChainGraphs>();
        graphs.Find("Trax.X.Parity").Returns(ParityOperationsOverHttpTests.RoutedGraph);

        var graph = await new OperationsQueries().GetRunGraph(run, _factory, graphs, default);
        var steps = await new OperationsQueries().GetJunctionRuns(run, _factory, default);

        graph!.Train.Should().Be("Trax.X.Parity");
        graph.HasGraph.Should().BeTrue();
        graph.Hash.Should().Be(ParityOperationsOverHttpTests.RoutedGraph.Hash);
        graph
            .Should()
            .BeEquivalentTo(
                RunGraphs.Match(run, "Trax.X.Parity", graphs.Find("Trax.X.Parity"), steps)
            );
        graph.Nodes[0].State.Should().Be(RunNodeState.Completed);
        graph.Nodes[1].TrackTaken.Should().Be("Fast");
        graph.Nodes[1].Tracks[0].Nodes[0].State.Should().Be(RunNodeState.Failed);
        graph.Nodes[2].State.Should().Be(RunNodeState.NotReached);
        graph.UnmatchedSteps.Select(s => s.Name).Should().Equal("Legacy");
    }

    [Test]
    public async Task RunGraph_OfAnIdWithNoRun_IsNull_AndOfAnUnregisteredTrain_HasNoGraph()
    {
        var graphs = Substitute.For<ITrainChainGraphs>();
        (await new OperationsQueries().GetRunGraph(424242, _factory, graphs, default))
            .Should()
            .BeNull();

        var run = await SeedRun();
        await SeedSteps(
            run,
            ("Fetch", JunctionRunKind.Junction, JunctionRunState.Completed, "Fetch#0", null)
        );
        var graph = await new OperationsQueries().GetRunGraph(run, _factory, graphs, default);

        graph!.HasGraph.Should().BeFalse();
        graph.Nodes.Should().BeEmpty();
        graph.UnmatchedSteps.Should().ContainSingle().Which.NodeId.Should().Be("Fetch#0");
    }

    private async Task SeedSteps(
        long run,
        params (
            string Name,
            JunctionRunKind Kind,
            JunctionRunState State,
            string? NodeId,
            string? Answer
        )[] steps
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var position = 0;
        foreach (var step in steps)
            db.JunctionRuns.Add(
                new JunctionRun
                {
                    MetadataId = run,
                    Position = position++,
                    Kind = step.Kind,
                    Name = step.Name,
                    State = step.State,
                    StartedAt = DateTime.UtcNow,
                    QuestionKey = step.Kind == JunctionRunKind.Junction ? null : step.Name,
                    Answer = step.Answer,
                    NodeId = step.NodeId,
                }
            );
        await db.SaveChanges(default);
    }

    #endregion

    #region Trains

    [Test]
    public void Trains_CarryWhetherTheyHaveAQueueSubjectKey()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                Registration(typeof(ISubjectTrain), true),
                Registration(typeof(IPlainTrain), false),
            ]);

        var trains = new OperationsQueries().GetTrains(discovery);

        trains
            .Single(t => t.FullName == typeof(ISubjectTrain).FullName)
            .HasQueueSubjectKey.Should()
            .BeTrue();
        trains
            .Single(t => t.FullName == typeof(IPlainTrain).FullName)
            .HasQueueSubjectKey.Should()
            .BeFalse();
    }

    public interface ISubjectTrain;

    public interface IPlainTrain;

    private static TrainRegistration Registration(Type serviceType, bool hasSubjectKey) =>
        new()
        {
            ServiceType = serviceType,
            ImplementationType = serviceType,
            InputType = typeof(Unit),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = serviceType.Name,
            ImplementationTypeName = serviceType.Name,
            InputTypeName = nameof(Unit),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            HasQueueSubjectKey = hasSubjectKey,
            GraphQLOperations = GraphQLOperation.Run,
        };

    #endregion

    private async Task<long> SeedRun(Action<Metadata>? configure = null)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Parity",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        configure?.Invoke(meta);
        await db.Track(meta);
        await db.SaveChanges(default);
        return meta.Id;
    }

    /// <summary>
    /// A failed run with this reason, and junction and host when given. The model sets the
    /// failure only from an exception, so the columns are written directly.
    /// </summary>
    private async Task<long> SeedFailedRun(
        string reason,
        string? junction = null,
        string? hostName = null
    )
    {
        var id = await SeedRun(m => m.HostName = hostName);
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE trax.metadata SET train_state = 'failed', failure_reason = {reason}, failure_junction = {junction} WHERE id = {id}"
        );
        return id;
    }

    private async Task<string> ExternalIdOf(long id)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        return await db.Metadatas.Where(m => m.Id == id).Select(m => m.ExternalId).SingleAsync();
    }

    private async Task<long> SeedGroup()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var group = new ManifestGroup
        {
            Name = $"group-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(group);
        await db.SaveChanges(default);
        return group.Id;
    }

    private async Task<long> SeedManifest(long groupId, string? name = null)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IPlainTrain) });
        manifest.ManifestGroupId = groupId;
        if (name is not null)
            manifest.Name = name;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return manifest.Id;
    }

    private async Task SeedDeadLetter(long manifestId)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var manifest = await db.Manifests.SingleAsync(m => m.Id == manifestId);
        await db.Track(
            DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = "failed",
                    RetryCount = 3,
                }
            )
        );
        await db.SaveChanges(default);
    }

    private async Task<long> SeedEntry(CreateWorkQueue create)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(create);
        await db.Track(entry);
        await db.SaveChanges(default);
        return entry.Id;
    }

    private async Task SeedLog(long metadataId, string message, string category)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO trax.log (metadata_id, event_id, level, message, category) VALUES ({metadataId}, 0, {LogLevel.Information.ToString().ToLowerInvariant()}::trax.log_level, {message}, {category})"
        );
    }

    private async Task SeedDecision(long metadataId, string key, int occurrence, string answer)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        db.RecordedDecisions.Add(
            new RecordedDecision
            {
                MetadataId = metadataId,
                QuestionKey = key,
                Occurrence = occurrence,
                Fingerprint = new string('0', 64),
                Kind = "choice",
                Question = "{}",
                Answer = answer,
                DecidedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChanges(default);
    }
}
