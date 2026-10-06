using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// One SLA test per read the operations surface gained to match the dashboard: the new filters on
/// executions, manifests, the work queue, dead letters and logs, a run's recorded decisions, and
/// the per-process effects, log level and version reads. The list reads call the resolvers
/// directly, as <see cref="AdminEndpointStressTests"/> does, so the measured cost is the query
/// itself; the per-process reads run through the schema, since their cost is the pipeline.
/// </summary>
/// <remarks>
/// Each filter is measured on the shape that costs it most at the seed's scale: a term or key
/// that matches rarely (the read walks furthest before it fills a page), one that matches a large
/// share of the table (the exact count is widest), and deep oldest-first pages by cursor.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class ParityEndpointStressTests : StressTestSetup
{
    protected override void ConfigureServices(IServiceCollection services) =>
        AddOperationsGraphQL(services);

    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    private static ISqlDialect Dialect(IServiceProvider sp) => sp.GetRequiredService<ISqlDialect>();

    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    /// <summary>A mid-table manifest: the seed gives it runs, queue entries and dead letters.</summary>
    private const long ManifestId = 2_501;

    #region Executions

    [Test]
    public async Task Executions_ByExternalId_WithinBudget()
    {
        var externalId = (Profile.Metadata / 2).ToString().PadLeft(32, '0');
        await MeasureAsync(
            "operations.executions (externalId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    externalId: externalId,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().ContainSingle().Which.Id.Should().Be(Profile.Metadata / 2);
                page.TotalCount.Should().Be(1);
            }
        );
    }

    [Test]
    public async Task Executions_ByParentId_FirstPage_WithinBudget()
    {
        // The seed makes one run in a hundred a child of run 1: tens of thousands of children.
        await MeasureAsync(
            "operations.executions (parentId, first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    parentId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25).And.OnlyContain(e => e.ParentId == 1);
                page.TotalCount.Should().BeGreaterThan(10_000);
            }
        );
    }

    [Test]
    public async Task Executions_ByParentId_OldestFirstDeepCursor_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (parentId, oldest first, deep cursor)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 100,
                    order: SortOrder.Oldest,
                    afterId: Profile.Metadata * 9 / 10,
                    parentId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty().And.OnlyContain(e => e.ParentId == 1);
                page.Items.Select(e => e.Id).Should().BeInAscendingOrder();
            }
        );
    }

    [Test]
    public async Task Executions_ByHostName_WithinBudget()
    {
        // A host ran a quarter of the seed: the widest exact count among the new filters.
        await MeasureAsync(
            "operations.executions (hostName)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    hostName: "stress-host-1",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().BeGreaterThan((int)(Profile.Metadata / 5));
            }
        );
    }

    [Test]
    public async Task Executions_ByHostName_AndRunning_WithinBudget()
    {
        // What an operator asks of one host: what is it running now.
        await MeasureAsync(
            "operations.executions (hostName + trainState=IN_PROGRESS)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    trainState: Effect.Enums.TrainState.InProgress,
                    hostName: "stress-host-1",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should()
                    .OnlyContain(e => e.TrainState == Effect.Enums.TrainState.InProgress);
            }
        );
    }

    #endregion

    #region Executions by failure

    private Task<TimeSpan> MeasureExecutions(
        string label,
        Func<PagedResult<ExecutionSummary>, bool> check,
        string? failureReasonContains = null,
        string? failureJunction = null,
        SortOrder order = SortOrder.Newest,
        long? afterId = null,
        string? hostName = null,
        int take = 25
    ) =>
        MeasureAsync(
            label,
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: take,
                    order: order,
                    afterId: afterId,
                    hostName: hostName,
                    failureReasonContains: failureReasonContains,
                    failureJunction: failureJunction,
                    sqlDialect: Dialect(sp)
                );
                TestContext.Out.WriteLine(
                    $"  {label}: {page.Items.Count} rows, total {page.TotalCount}"
                        + (page.IsEstimatedCount ? " (estimate)" : "")
                        + (page.IsCountCapped ? " (capped)" : "")
                );
                check(page).Should().BeTrue($"the page of {label} is the one asked for");
            }
        );

    private static bool Carries(ExecutionSummary e, string term) =>
        e.FailureReason?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsNewestFirst(PagedResult<ExecutionSummary> page) =>
        page.Items.Select(e => e.Id).SequenceEqual(page.Items.Select(e => e.Id).OrderDescending());

    /// <summary>How many seeded runs carry <see cref="BulkSeeder.RareFailureTerm"/>.</summary>
    private static long RareFailures => (Profile.Metadata - 4) / 99_999 + 1;

    [Test]
    public async Task Executions_FailureReasonContains_CommonTerm_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureReasonContains, common term)",
            // Two runs in nine carry it, old and new alike: the page fills at once and the count
            // stops at the cap and says so.
            page =>
                page.Items.Count == 25
                && page.Items.All(e => Carries(e, BulkSeeder.CommonFailureTerm))
                && IsNewestFirst(page)
                && page.TotalCount == OperationsQueries.FailureTextCountCap
                && page.IsCountCapped,
            failureReasonContains: BulkSeeder.CommonFailureTerm
        );

    [Test]
    public async Task Executions_FailureReasonContains_TermOnlyInOldRows_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureReasonContains, common term found only in old rows)",
            // About 67,000 runs carry it, every one among the oldest 300,000, so a newest-first page
            // read in id order alone would walk past every newer run before it filled.
            page =>
                page.Items.Count == 25
                && page.Items.All(e =>
                    Carries(e, BulkSeeder.OldFailureTerm) && e.Id <= BulkSeeder.OldFailureIds
                )
                && IsNewestFirst(page)
                && page.IsCountCapped,
            failureReasonContains: BulkSeeder.OldFailureTerm
        );

    [Test]
    public async Task Executions_FailureReasonContains_RareTerm_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureReasonContains, a few dozen runs)",
            // The term's "%" and "_" match only themselves; unescaped, "_" would match any
            // character and the rest of the table's reasons would still not match, so the exact
            // count is what proves it.
            page =>
                page.Items.Count == (int)Math.Min(25, RareFailures)
                && page.Items.All(e => Carries(e, BulkSeeder.RareFailureTerm))
                && IsNewestFirst(page)
                && page.TotalCount == RareFailures
                && !page.IsCountCapped,
            failureReasonContains: BulkSeeder.RareFailureTerm
        );

    [Test]
    public async Task Executions_FailureReasonContains_AbsentTerm_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureReasonContains, no match)",
            page => page.Items.Count == 0 && page.TotalCount == 0 && !page.IsCountCapped,
            failureReasonContains: "no such failure anywhere"
        );

    [Test]
    public async Task Executions_FailureReasonContains_OldestFirstDeepCursor_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureReasonContains, oldest first, deep cursor)",
            page =>
                page.Items.Count == 100
                && page.Items.All(e =>
                    Carries(e, BulkSeeder.CommonFailureTerm) && e.Id > Profile.Metadata * 9 / 10
                )
                && page.Items.Select(e => e.Id).SequenceEqual(page.Items.Select(e => e.Id).Order()),
            failureReasonContains: BulkSeeder.CommonFailureTerm,
            order: SortOrder.Oldest,
            afterId: Profile.Metadata * 9 / 10,
            take: 100
        );

    [Test]
    public async Task Executions_FailureReasonContains_AndHostName_WithinBudget() =>
        // The old-only term on one host: two filters, neither of which fills a page near the
        // newest runs.
        await MeasureExecutions(
            "operations.executions (failureReasonContains, old-only term + hostName)",
            page =>
                page.Items.Count == 25
                && page.Items.All(e =>
                    Carries(e, BulkSeeder.OldFailureTerm) && e.HostName == "stress-host-1"
                )
                && IsNewestFirst(page),
            failureReasonContains: BulkSeeder.OldFailureTerm,
            hostName: "stress-host-1"
        );

    [Test]
    public async Task Executions_FailureJunction_WithinBudget() =>
        // One junction in twenty: about 33,000 failed runs, counted exactly.
        await MeasureExecutions(
            "operations.executions (failureJunction)",
            page =>
                page.Items.Count == 25
                && page.Items.All(e => e.FailureJunction == BulkSeeder.FailureJunctionPrefix + "7")
                && IsNewestFirst(page)
                && page.TotalCount > 25_000
                && !page.IsCountCapped,
            failureJunction: BulkSeeder.FailureJunctionPrefix + "7"
        );

    [Test]
    public async Task Executions_FailureJunction_AndOldOnlyTerm_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureJunction + failureReasonContains, old-only term)",
            page =>
                page.Items.Count == 25
                && page.Items.All(e =>
                    e.FailureJunction == BulkSeeder.FailureJunctionPrefix + "7"
                    && Carries(e, BulkSeeder.OldFailureTerm)
                )
                && IsNewestFirst(page),
            failureReasonContains: BulkSeeder.OldFailureTerm,
            failureJunction: BulkSeeder.FailureJunctionPrefix + "7"
        );

    [Test]
    public async Task Executions_FailureJunction_Absent_WithinBudget() =>
        await MeasureExecutions(
            "operations.executions (failureJunction, no match)",
            page => page.Items.Count == 0 && page.TotalCount == 0,
            failureJunction: "NoSuchJunction"
        );

    #endregion

    #region Manifests, work queue, dead letters

    [Test]
    public async Task Manifests_HideAdminTrains_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifests (hideAdminTrains)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetManifests(
                    Factory(sp),
                    ct,
                    take: 25,
                    hideAdminTrains: true,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().Be(Profile.Manifests);
            }
        );
    }

    [Test]
    public async Task WorkQueues_BySubjectKey_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueues (subjectKey)",
            ListBudget,
            async (sp, ct) =>
            {
                await ReadFieldStressTests.EnsureSubjectRowsAsync(sp, ct);
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    subjectKey: "subject-7",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty().And.OnlyContain(q => q.SubjectKey == "subject-7");
            }
        );
    }

    [Test]
    public async Task WorkQueues_ByAbsentSubjectKey_WithinBudget()
    {
        // Nothing matches, so the read cannot stop early: the cost of a subject nobody queued.
        await MeasureAsync(
            "operations.workQueue.workQueues (subjectKey, no match)",
            ListBudget,
            async (sp, ct) =>
            {
                await ReadFieldStressTests.EnsureSubjectRowsAsync(sp, ct);
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    subjectKey: "no-such-subject",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().BeEmpty();
                page.TotalCount.Should().Be(0);
            }
        );
    }

    [Test]
    public async Task WorkQueues_ByManifestId_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueues (manifestId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    manifestId: ManifestId,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty().And.OnlyContain(q => q.ManifestId == ManifestId);
            }
        );
    }

    [Test]
    public async Task DeadLetters_ByManifestId_WithinBudget()
    {
        await MeasureAsync(
            "operations.deadLetters.deadLetters (manifestId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new DeadLetterQueries().GetDeadLetters(
                    Factory(sp),
                    ct,
                    take: 25,
                    manifestId: ManifestId
                );
                page.Items.Should().NotBeEmpty().And.OnlyContain(d => d.ManifestId == ManifestId);
                page.TotalCount.Should().BeGreaterThan(25);
            }
        );
    }

    #endregion

    #region Logs

    private Task<TimeSpan> MeasureLogs(
        string label,
        Func<PagedResult<LogEntry>, bool> check,
        string? messageContains = null,
        string? categoryContains = null,
        SortOrder order = SortOrder.Newest,
        long? metadataId = null,
        long? afterId = null,
        int take = 25
    ) =>
        MeasureAsync(
            label,
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new LogQueries().GetLogs(
                    Operations(sp),
                    Factory(sp),
                    ct,
                    take: take,
                    metadataId: metadataId,
                    afterId: afterId,
                    messageContains: messageContains,
                    categoryContains: categoryContains,
                    order: order,
                    sqlDialect: Dialect(sp)
                );
                TestContext.Out.WriteLine(
                    $"  {label}: {page.Items.Count} rows, total {page.TotalCount}"
                        + (page.IsEstimatedCount ? " (estimate)" : "")
                        + (page.IsCountCapped ? " (capped)" : "")
                );
                check(page).Should().BeTrue($"the page of {label} is the one asked for");
            }
        );

    [Test]
    public async Task Logs_MessageContains_CommonTerm_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (messageContains, common term)",
            // "99" is in about one seeded message in sixteen, old and new alike: the page fills at
            // once and the count stops at the cap and says so.
            page =>
                page.Items.Count == 25
                && page.TotalCount == OperationsService.LogCountCap
                && page.IsCountCapped,
            messageContains: "99"
        );

    [Test]
    public async Task Logs_MessageContains_TermOnlyInOldRows_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (messageContains, common term found only in old rows)",
            // "message 12" matches about 110,000 seeded rows, every one of them among the oldest
            // 1,300,000, so a newest-first page reads past every newer row before it fills.
            page => page.Items.Count == 25 && page.IsCountCapped,
            messageContains: "MESSAGE 12"
        );

    [Test]
    public async Task Logs_MessageContains_RareTerm_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (messageContains, one row in the table)",
            // Seven digits match only themselves at the default profile's 3M rows.
            page => page.Items.Count >= 1 && !page.IsCountCapped,
            messageContains: $"stress log message {Profile.Log / 2 + 7}"
        );

    [Test]
    public async Task Logs_MessageContains_AbsentTerm_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (messageContains, no match)",
            page => page.Items.Count == 0 && page.TotalCount == 0,
            messageContains: "no such text anywhere"
        );

    [Test]
    public async Task Logs_CategoryContains_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (categoryContains)",
            page =>
                page.Items.Count == 25
                && page.IsCountCapped
                && page.Items.All(l => l.Category.Contains("Category1")),
            categoryContains: "category1"
        );

    [Test]
    public async Task Logs_OldestFirst_DeepCursor_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (oldest first, deep cursor)",
            page =>
                page.Items.Count == 100
                && page.Items.Select(l => l.Id).SequenceEqual(page.Items.Select(l => l.Id).Order())
                && page.Items[0].Id > Profile.Log * 9 / 10,
            order: SortOrder.Oldest,
            afterId: Profile.Log * 9 / 10,
            take: 100
        );

    [Test]
    public async Task Logs_OneRun_OldestFirst_DeepCursor_WithinBudget() =>
        // A run's log as its page reads it: oldest first, paged forward through a chatty run.
        await MeasureLogs(
            "operations.logs.logs (metadataId, oldest first, deep cursor)",
            page => page.Items.Count > 0 && page.Items.All(l => l.MetadataId == 1),
            order: SortOrder.Oldest,
            metadataId: 1,
            afterId: Profile.Log * 9 / 10,
            take: 100
        );

    [Test]
    public async Task Logs_OneRun_MessageContains_WithinBudget() =>
        await MeasureLogs(
            "operations.logs.logs (metadataId + messageContains)",
            page => page.Items.All(l => l.MetadataId == 1),
            messageContains: "message 3",
            metadataId: 1
        );

    #endregion

    #region Decisions

    [Test]
    public async Task Decisions_ARun_WithinBudget()
    {
        var run = BulkSeeder.DecisionRunOf(Profile.Decisions / 2);
        await MeasureAsync(
            "operations.decisions (a run's few decisions)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetDecisions(run, Operations(sp), ct);
                page.Items.Should().HaveCount(BulkSeeder.DecisionsPerRun);
                page.Items.Should().OnlyContain(d => d.MetadataId == run);
            }
        );
    }

    [Test]
    public async Task Decisions_ALongHistory_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.decisions (2,000-decision run, first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetDecisions(
                    BulkSeeder.ChattyDecisionRun,
                    Operations(sp),
                    ct
                );
                page.Items.Should().HaveCount(50);
                page.NextCursor.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task Decisions_ALongHistory_LastPages_WithinBudget()
    {
        // A page deep into the run also reads which questions routed before it, to know whether
        // the track it is on is withheld: the widest read this field makes.
        long cursor;
        using (var scope = Services.CreateScope())
        {
            var first = await new OperationsQueries().GetDecisions(
                BulkSeeder.ChattyDecisionRun,
                Operations(scope.ServiceProvider),
                CancellationToken.None,
                take: 500
            );
            var page = first;
            for (var i = 0; i < 2; i++)
                page = await new OperationsQueries().GetDecisions(
                    BulkSeeder.ChattyDecisionRun,
                    Operations(scope.ServiceProvider),
                    CancellationToken.None,
                    afterId: page.NextCursor,
                    take: 500
                );
            cursor = page.NextCursor!.Value;
        }

        await MeasureAsync(
            "operations.decisions (2,000-decision run, last page of 500)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetDecisions(
                    BulkSeeder.ChattyDecisionRun,
                    Operations(sp),
                    ct,
                    afterId: cursor,
                    take: 500
                );
                page.Items.Should().HaveCount(500);
                page.Items.Should().OnlyContain(d => !d.TrackWithheld);
            }
        );
    }

    #endregion

    #region Per-process reads, through the schema

    private async Task MeasureField(string label, string document, Action<JsonElement> check) =>
        await MeasureAsync(
            label,
            TrivialBudget,
            async (_, ct) => check(await OperationsFieldAsync(document, ct))
        );

    [Test]
    public async Task Effects_WithFields_WithinBudget() =>
        await MeasureField(
            "operations.effects (+fields)",
            "{ operations { effects { fullName isConfigurable fields { name kind sensitive hasValue value hint } } } }",
            effects =>
                effects
                    .EnumerateArray()
                    .Should()
                    .Contain(e => e.GetProperty("fields").GetArrayLength() > 0)
        );

    [Test]
    public async Task ConfigLogLevels_WithinBudget() =>
        await MeasureField(
            "operations.config.logLevels",
            "{ operations { config { logLevels { category level configuredLevel overridden } } } }",
            config => config.GetProperty("logLevels").ValueKind.Should().Be(JsonValueKind.Array)
        );

    [Test]
    public async Task ConfigVersion_WithinBudget() =>
        await MeasureField(
            "operations.config.version",
            "{ operations { config { version } } }",
            config => config.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace()
        );

    [Test]
    public async Task Trains_WithQueueSubjectKey_WithinBudget() =>
        await MeasureField(
            "operations.trains (+hasQueueSubjectKey)",
            "{ operations { trains(hideAdminTrains: true) { fullName hasQueueSubjectKey } } }",
            trains => trains.GetArrayLength().Should().BeGreaterThan(0)
        );

    #endregion
}
