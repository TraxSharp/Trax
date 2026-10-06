using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// SLA tests for the mutations the operations surface gained to match the dashboard: the batch
/// triggers and the batch group cancel at a full batch of 1000 ids, a dashboard page's selection of
/// groups, and the per-process effect and log level writes. Each runs through the schema's request
/// executor as a caller holding the operations gate's role, against the same seed as
/// <see cref="AdminEndpointStressTests"/>, and puts back what it changed.
/// </summary>
/// <remarks>
/// A batch trigger queues each manifest as <c>triggerManifest</c> does, one save each, so its cost
/// grows with the manifests it reaches, and a group cancel's with the runs it flags. The full
/// batches reach a thousand manifests, or flag one group's runs, and have budgets of their own,
/// measured. The seed is pathological for a cancel: two in nine of its 3,000,000 runs are pending
/// or running, so a page of 25 groups flags about 83,000 of them.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class ParityMutationStressTests : StressTestSetup
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        AddOperationsGraphQL(services);
        // A host's Logging section, so there are configured categories for setLogLevels to set.
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Logging:LogLevel:Default"] = "Warning",
                        ["Logging:LogLevel:Trax"] = "Information",
                    }
                )
                .Build()
        );
    }

    /// <summary>
    /// Budget for a batch trigger that reaches 1000 manifests, each queued with its own save, the
    /// way <c>triggerManifest</c> queues one: measured at about 3 s for new entries and 0.6 s when
    /// every manifest already had one, with headroom for a slower machine. Well inside
    /// HotChocolate's 30 s execution timeout, which is what it must never approach.
    /// </summary>
    private static readonly TimeSpan FullManifestBatchBudget = TimeSpan.FromSeconds(5);

    /// <summary>A batch trigger reaching a dashboard page of groups' manifests (625 of them).</summary>
    private static readonly TimeSpan GroupPageTriggerBudget = TimeSpan.FromSeconds(2.5);

    /// <summary>A dashboard page of groups.</summary>
    private const int GroupPage = 25;

    private Task<JsonElement> Mutate(string field, CancellationToken ct, params string[] path)
    {
        var body = "{ " + field + " }";
        foreach (var segment in path.Reverse())
            body = "{ " + segment + " " + body + " }";
        return OperationsFieldAsync("mutation { operations " + body + " }", ct, path);
    }

    private static string Ids(IEnumerable<long> ids) => "[" + string.Join(",", ids) + "]";

    private static string Message(JsonElement payload) =>
        payload.GetProperty("message").GetString() ?? "";

    /// <summary>Removes the queued entries the triggers made; the seed queues none for a manifest.</summary>
    private static Task DeleteQueuedManifestEntries() =>
        ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE status = 'queued' AND manifest_id IS NOT NULL"
        );

    [OneTimeTearDown]
    public async Task RestoreSeed()
    {
        await DeleteQueuedManifestEntries();
        await ExecSqlAsync(
            "UPDATE trax.metadata SET cancel_requested = false WHERE cancel_requested"
        );
        // A group cancel rewrites tens of thousands of runs and its restore as many again. The
        // metrics reads lean on index-only scans, so the visibility map is reset as the seed
        // leaves it, whatever fixture runs next.
        await ExecSqlAsync("VACUUM (ANALYZE, PARALLEL 0) trax.metadata, trax.work_queue");
    }

    #region Batch triggers

    [Test]
    public async Task TriggerManifests_FullBatch_WithinBudget()
    {
        var ids = Enumerable
            .Range(0, OperationsService.MaxBatchSize)
            .Select(i => (long)(Profile.Manifests / 2 - OperationsService.MaxBatchSize / 2 + i))
            .ToArray();

        await MeasureWriteAsync(
            "operations.triggerManifests (1000 ids)",
            FullManifestBatchBudget,
            DeleteQueuedManifestEntries,
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"triggerManifests(ids: {Ids(ids)}) {{ success queued matched message }}",
                    ct
                );
                payload.GetProperty("queued").GetInt32().Should().Be(ids.Length, Message(payload));
            }
        );
    }

    [Test]
    public async Task TriggerManifests_FullBatch_AllAlreadyQueued_AskingAfresh_WithinBudget()
    {
        // Each manifest already holds a queued entry, so each is brought forward rather than queued.
        var ids = Enumerable
            .Range(0, OperationsService.MaxBatchSize)
            .Select(i => (long)(Profile.Manifests / 2 - OperationsService.MaxBatchSize / 2 + i))
            .ToArray();

        await MeasureWriteAsync(
            "operations.triggerManifests (1000 ids, already queued, askAfresh)",
            FullManifestBatchBudget,
            async () =>
            {
                await DeleteQueuedManifestEntries();
                await ExecSqlAsync(
                    "INSERT INTO trax.work_queue (external_id, train_name, status, created_at, "
                        + "scheduled_at, priority, dispatch_attempts, manifest_id, confirmed_at) "
                        + "SELECT 'wq-parity-' || id, name, 'queued', now(), now() + interval '1 hour', "
                        + "0, 0, id, now() FROM trax.manifest "
                        + $"WHERE id IN ({string.Join(',', ids)})"
                );
            },
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"triggerManifests(ids: {Ids(ids)}, askAfresh: true) "
                        + "{ success queued alreadyQueued message }",
                    ct
                );
                payload
                    .GetProperty("alreadyQueued")
                    .GetInt32()
                    .Should()
                    .Be(ids.Length, Message(payload));
            },
            DeleteQueuedManifestEntries
        );
    }

    [Test]
    public async Task TriggerGroups_APage_WithinBudget()
    {
        var ids = Enumerable.Range(1, GroupPage).Select(i => (long)i).ToArray();

        await MeasureWriteAsync(
            "operations.triggerGroups (a page of 25 groups)",
            GroupPageTriggerBudget,
            DeleteQueuedManifestEntries,
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"triggerGroups(ids: {Ids(ids)}) {{ success matched queued message }}",
                    ct
                );
                payload.GetProperty("matched").GetInt32().Should().Be(GroupPage);
                payload
                    .GetProperty("queued")
                    .GetInt32()
                    .Should()
                    .BeGreaterThan(0, Message(payload));
            }
        );
    }

    [Test]
    public async Task TriggerGroups_FullBatch_WithinBudget()
    {
        // 1000 ids at the cap: the last 40 of the seed's groups, whose 1000 manifests are as many
        // as triggerManifests' full batch reaches, and 960 ids that name no group.
        var first = Profile.Groups - 39;
        var ids = Enumerable
            .Range(first, OperationsService.MaxBatchSize)
            .Select(i => (long)i)
            .ToArray();

        await MeasureWriteAsync(
            "operations.triggerGroups (1000 ids, 40 groups)",
            FullManifestBatchBudget,
            DeleteQueuedManifestEntries,
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"triggerGroups(ids: {Ids(ids)}) {{ success matched queued skipped message }}",
                    ct
                );
                payload.GetProperty("matched").GetInt32().Should().Be(40);
                payload
                    .GetProperty("skipped")
                    .GetInt32()
                    .Should()
                    .Be(OperationsService.MaxBatchSize - 40);
                payload
                    .GetProperty("queued")
                    .GetInt32()
                    .Should()
                    .Be(40 * Profile.Manifests / Profile.Groups, Message(payload));
            }
        );
    }

    #endregion

    #region Batch group cancel

    private static Task ClearCancellations() =>
        ExecSqlAsync("UPDATE trax.metadata SET cancel_requested = false WHERE cancel_requested");

    /// <summary>
    /// What a group cancel may cost per run it flags. The query is one set-based update, so what
    /// grows with the page is Postgres rewriting each flagged metadata row, and every one of the
    /// table's indexes with it. The budget therefore scales with the runs flagged, on top of a
    /// fixed allowance for the statement itself.
    /// </summary>
    private static readonly TimeSpan CancelBudgetPerRun = TimeSpan.FromMilliseconds(0.1);

    [Test]
    public async Task CancelGroups_APage_WithinBudget()
    {
        var ids = Enumerable.Range(1, GroupPage).Select(i => (long)i).ToArray();
        var flagged = await ScalarAsync<long>(
            $"""
            SELECT count(*) FROM trax.metadata
            WHERE train_state IN ('pending', 'in_progress')
              AND manifest_id IN (
                SELECT id FROM trax.manifest WHERE manifest_group_id BETWEEN 1 AND {GroupPage})
            """
        );
        flagged.Should().BeGreaterThan(0, "the seed puts active runs in the first groups");

        await MeasureWriteAsync(
            $"operations.cancelGroups (a page of 25 groups, {flagged} runs)",
            ListBudget + CancelBudgetPerRun * flagged,
            ClearCancellations,
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"cancelGroups(ids: {Ids(ids)}) {{ success count message }}",
                    ct
                );
                payload.GetProperty("count").GetInt32().Should().Be((int)flagged, Message(payload));
            }
        );
    }

    [Test]
    public async Task CancelGroups_FullBatch_WithinBudget()
    {
        // 1000 ids at the cap, one of them a seeded group: the id list's own cost on top of what
        // cancelGroup does for one group.
        var ids = Enumerable
            .Range(Profile.Groups, OperationsService.MaxBatchSize)
            .Select(i => (long)i)
            .ToArray();

        await MeasureWriteAsync(
            "operations.cancelGroups (1000 ids, 1 group)",
            ListBudget,
            ClearCancellations,
            async (_, ct) =>
            {
                var payload = await Mutate(
                    $"cancelGroups(ids: {Ids(ids)}) {{ success count message }}",
                    ct
                );
                payload.GetProperty("count").GetInt32().Should().BeGreaterThan(0, Message(payload));
            }
        );
    }

    #endregion

    #region Per-process writes

    [Test]
    public async Task ConfigureEffect_WithinBudget()
    {
        var effect = Services
            .CreateScope()
            .ServiceProvider.GetRequiredService<IEffectSettingsService>()
            .GetEffects()
            .First(e => e.Fields.Any(f => f.Name == "SaveOutputs"));

        await MeasureAsync(
            "operations.configureEffect",
            TrivialBudget,
            async (_, ct) =>
            {
                foreach (var value in new[] { "false", "true" })
                {
                    var payload = await Mutate(
                        $"configureEffect(fullName: \"{effect.FullName}\", values: "
                            + $"[{{ name: \"SaveOutputs\", value: \"{value}\" }}]) "
                            + "{ success count message errors { field message } }",
                        ct
                    );
                    payload.GetProperty("success").GetBoolean().Should().BeTrue(Message(payload));
                }
            }
        );
    }

    [Test]
    public async Task SetLogLevels_WithinBudget()
    {
        // Sets a configured category and puts it back, as the dashboard's Save does.
        await MeasureAsync(
            "operations.config.setLogLevels",
            TrivialBudget,
            async (_, ct) =>
            {
                var levels = await OperationsFieldAsync(
                    "{ operations { config { logLevels { category level } } } }",
                    ct,
                    "config"
                );
                levels.GetArrayLength().Should().BeGreaterThan(0);

                var category = levels[0].GetProperty("category").GetString();
                var level = levels[0].GetProperty("level").GetString()!.ToUpperInvariant();
                foreach (var target in new[] { "DEBUG", level })
                {
                    var payload = await Mutate(
                        $"setLogLevels(levels: [{{ category: \"{category}\", level: {target} }}]) "
                            + "{ success count message }",
                        ct,
                        "config"
                    );
                    payload.GetProperty("success").GetBoolean().Should().BeTrue(Message(payload));
                }
            }
        );
    }

    #endregion
}
