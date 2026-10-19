using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// The operator's view of state-machine instances at millions of rows: the list under each filter
/// an operator uses (none, a machine, a machine and state, an owner kind), its deepest offset, one
/// instance, and the counts by state. The list reads call the resolver, which reads its page and
/// its capped total through <see cref="IOperationsService"/> exactly as the dashboard's State
/// machines page does, so the measured cost is both surfaces' cost. One read runs through the
/// schema to time the whole pipeline, as do one instance's invoked runs and the operator's cancel,
/// each through the service method the dashboard's instance page calls.
/// </summary>
/// <remarks>
/// The seed spreads <see cref="StressProfile.SnapshotDrafts"/> instances evenly over six machines
/// (two system-owned) and eight states, written over fourteen days. Every page carries the
/// context-free fields only, so each check also confirms no context reached the response.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class MachineInstanceStressTests : StressTestSetup
{
    /// <summary>
    /// Budget for the counts by machine, state and owner kind: every instance is counted, so it
    /// grows with the table. It is read once when the page opens and on each poll, like the
    /// dashboard metrics, and served from the listing index alone.
    /// </summary>
    private static readonly TimeSpan CountsBudget = TimeSpan.FromMilliseconds(450);

    protected override void ConfigureServices(IServiceCollection services) =>
        AddOperationsGraphQL(services);

    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    private static readonly string SystemMachine = BulkSeeder.SnapshotMachine(1);
    private static readonly string UserMachine = BulkSeeder.SnapshotMachine(4);

    private Task<TimeSpan> MeasureList(
        string label,
        Func<PagedResult<MachineInstance>, bool> check,
        string? machine = null,
        string? state = null,
        SnapshotOwnerKind? ownerKind = null,
        int skip = 0,
        int take = 25
    ) =>
        MeasureAsync(
            label,
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetMachineInstances(
                    Operations(sp),
                    ct,
                    machine,
                    state,
                    ownerKind,
                    skip,
                    take
                );
                TestContext.Out.WriteLine(
                    $"  {label}: {page.Items.Count} rows, total {page.TotalCount}"
                        + (page.IsCountCapped ? " (capped)" : "")
                );
                page.Items.Select(i => i.UpdatedAt)
                    .Should()
                    .BeInDescendingOrder("the list is newest first");
                check(page).Should().BeTrue($"the page of {label} is the one asked for");
            }
        );

    [Test]
    public Task Unfiltered_FirstPage_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (unfiltered)",
            p => p.Items.Count == 25 && p.IsCountCapped
        );

    [Test]
    public Task OneMachine_FirstPage_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (machine)",
            p => p.Items.Count == 25 && p.Items.All(i => i.Machine == UserMachine),
            machine: UserMachine
        );

    [Test]
    public Task OneMachineAndState_FirstPage_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (machine + state)",
            p =>
                p.Items.Count == 25
                && p.Items.All(i => i.Machine == SystemMachine && i.State == "State3"),
            machine: SystemMachine,
            state: "State3",
            ownerKind: SnapshotOwnerKind.System
        );

    [Test]
    public Task OneMachineAndState_DeepestOffset_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (machine + state, skip 10,000)",
            p => p.Items.Count == 100 && p.Skip == 10_000,
            machine: SystemMachine,
            state: "State3",
            skip: 10_000,
            take: 100
        );

    [Test]
    public Task SystemOwned_FirstPage_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (ownerKind SYSTEM)",
            p => p.Items.Count == 25 && p.Items.All(i => i.OwnerKind == SnapshotOwnerKind.System),
            ownerKind: SnapshotOwnerKind.System
        );

    [Test]
    public Task UnknownMachine_IsEmpty_WithinBudget() =>
        MeasureList(
            "operations.machineInstances (machine with no instances)",
            p => p.Items.Count == 0 && p.TotalCount == 0,
            machine: "Stress.Machines.Missing"
        );

    [Test]
    public async Task OneInstance_WithinBudget()
    {
        var first = (
            await Operations(Services)
                .GetMachineInstancesAsync(
                    new MachineInstanceQuery(SystemMachine, Take: 1),
                    CancellationToken.None
                )
        ).Items.Single();

        await MeasureAsync(
            "operations.machineInstance",
            ListBudget,
            async (sp, ct) =>
            {
                var instance = await new OperationsQueries().GetMachineInstance(
                    first.Machine,
                    SnapshotOwnerKind.System,
                    first.Id,
                    Operations(sp),
                    ct
                );
                instance!.RowId.Should().Be(first.RowId);
            }
        );
    }

    // How many runs the invoked-runs read finds for its instance: past the 50 it lists, so the
    // read stops at its cap.
    private const int InvokedRunsPerInstance = 200;

    [Test]
    public async Task OneInstance_WithItsInvokedRuns_ThroughTheSchema_WithinBudget()
    {
        var first = await FirstSystemInstanceAsync();
        var marker = "stress-invoked-" + first.Id.ToString("N")[..8];

        await MeasureWriteAsync(
            "operations.machineInstance { invokedRuns } (through the schema)",
            ListBudget,
            prepare: async () =>
            {
                await ExecSqlAsync(
                    $"DELETE FROM trax.metadata WHERE invoking_instance_id = '{first.Id}'"
                );
                await ExecSqlAsync(
                    "INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, "
                        + "failure_class, invoking_machine, invoking_instance_id, invoking_owner_kind) "
                        + $"SELECT '{marker}-' || g, 'Stress.IInvokedTrain', 'completed', "
                        + "now() - (g * interval '1 minute'), now() - (g * interval '1 minute') + interval '5 seconds', "
                        + $"'unclassified', '{first.Machine}', '{first.Id}', 'system' "
                        + $"FROM generate_series(1, {InvokedRunsPerInstance}) g"
                );
            },
            action: async (_, ct) =>
            {
                var instance = await OperationsFieldAsync(
                    "{ operations { machineInstance(machine: \""
                        + first.Machine
                        + "\", ownerKind: SYSTEM, id: \""
                        + first.Id
                        + "\") { rowId state isInvokedRunsCapped invokedRuns { id externalId name "
                        + "trainState startTime endTime failureClass isLive } } } }",
                    ct
                );
                instance.GetProperty("invokedRuns").GetArrayLength().Should().Be(50);
                instance.GetProperty("isInvokedRunsCapped").GetBoolean().Should().BeTrue();
                instance.GetRawText().Should().NotContain("stress-context-");
            },
            restore: () =>
                ExecSqlAsync($"DELETE FROM trax.metadata WHERE invoking_instance_id = '{first.Id}'")
        );
    }

    [Test]
    public async Task CancelOneInstance_ThroughTheSchema_WithinBudget()
    {
        var first = await FirstSystemInstanceAsync();
        var token = "stress-cancel-" + first.Id.ToString("N");

        async Task Restore()
        {
            await ExecSqlAsync($"DELETE FROM trax.work_queue WHERE external_id = '{token}'");
            await ExecSqlAsync(
                $"UPDATE trax.snapshot_draft SET invoke_token = NULL WHERE row_id = {first.RowId}"
            );
        }

        await MeasureWriteAsync(
            "operations.cancelMachineInstance (through the schema)",
            ListBudget,
            prepare: async () =>
            {
                await Restore();
                await ExecSqlAsync(
                    "INSERT INTO trax.work_queue (external_id, train_name, status, priority, created_at, "
                        + "invoking_machine, invoking_instance_id, invoking_owner_kind) "
                        + $"VALUES ('{token}', 'Stress.IInvokedTrain', 'queued', 0, now(), "
                        + $"'{first.Machine}', '{first.Id}', 'system')"
                );
                await ExecSqlAsync(
                    $"UPDATE trax.snapshot_draft SET invoke_token = '{token}' WHERE row_id = {first.RowId}"
                );
            },
            action: async (sp, ct) =>
            {
                var response = await ExecuteGraphQLAsync(
                    "mutation { operations { cancelMachineInstance(machine: \""
                        + first.Machine
                        + "\", ownerKind: SYSTEM, id: \""
                        + first.Id
                        + "\") { success outcome message } } }",
                    ct
                );
                response.TryGetProperty("errors", out _).Should().BeFalse(response.GetRawText());
                var result = response
                    .GetProperty("data")
                    .GetProperty("operations")
                    .GetProperty("cancelMachineInstance");
                result.GetProperty("success").GetBoolean().Should().BeTrue();
                result
                    .GetProperty("outcome")
                    .GetString()
                    .Should()
                    .Be("RUN_CANCELLED", "this host registers no machines to apply the outcome");
            },
            restore: Restore
        );
    }

    private async Task<MachineInstanceRecord> FirstSystemInstanceAsync() =>
        (
            await Operations(Services)
                .GetMachineInstancesAsync(
                    new MachineInstanceQuery(
                        SystemMachine,
                        OwnerKind: SnapshotOwnerKind.System,
                        Take: 1
                    ),
                    CancellationToken.None
                )
        ).Items.Single();

    // A history longer than any instance builds in practice, on one instance, for the history reads to page through.
    private const int DeepHistory = 100_000;

    private const string HistoryFields =
        "nextCursor items { revision kind trigger actor fromState toState at previousAt "
        + "startedRun { executionId trainState workQueueEntryId } startedChild { machine id rowId } }";

    private static string HistoryDocument(MachineInstanceRecord instance, string arguments) =>
        "{ operations { machineInstance(machine: \""
        + instance.Machine
        + "\", ownerKind: SYSTEM, id: \""
        + instance.Id
        + "\") { transitions("
        + arguments
        + ") { "
        + HistoryFields
        + " } } } }";

    // A system instance of the system machine that the seed gave a history, or one it did not.
    private async Task<MachineInstanceRecord> SystemInstanceAsync(bool withHistory)
    {
        var id = await ScalarAsync<string>(
            "SELECT d.id::text FROM trax.snapshot_draft d "
                + $"WHERE d.machine = '{SystemMachine}' AND d.owner_kind = 'system' AND "
                + (withHistory ? "" : "NOT ")
                + "EXISTS (SELECT 1 FROM trax.snapshot_transition t WHERE t.draft_row_id = d.row_id) "
                + "LIMIT 1"
        );
        return (
            await Operations(Services)
                .GetMachineInstanceAsync(
                    new MachineInstanceKey(SystemMachine, SnapshotOwnerKind.System, Guid.Parse(id)),
                    CancellationToken.None
                )
        )!;
    }

    [Test]
    public async Task OneInstancesHistory_ThroughTheSchema_WithinBudget()
    {
        // The seed gives half of every machine's instances a history.
        var instance = await SystemInstanceAsync(withHistory: true);
        (await ScalarAsync<long>("SELECT count(*) FROM trax.snapshot_transition"))
            .Should()
            .BeGreaterThan(Profile.SnapshotDrafts, "the history table is seeded at scale");

        await MeasureAsync(
            "operations.machineInstance { transitions } (through the schema)",
            ListBudget,
            async (_, ct) =>
            {
                var instanceField = await OperationsFieldAsync(
                    HistoryDocument(instance, "first: 25"),
                    ct
                );
                var page = instanceField.GetProperty("transitions");
                page.GetProperty("items")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("revision").GetInt64())
                    .Should()
                    .Equal(
                        Enumerable
                            .Range(1, BulkSeeder.TransitionsPerInstance)
                            .Reverse()
                            .Select(r => (long)r),
                        "newest first"
                    );
                page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
                instanceField.GetRawText().Should().NotContain("stress-context-");
                instanceField
                    .GetRawText()
                    .Should()
                    .NotContain("stress-history-run-", "never a token");
            }
        );
    }

    [Test]
    public async Task ADeepHistory_FirstAndDeepPages_WithinBudget()
    {
        var instance = await SystemInstanceAsync(withHistory: false);

        Task Clear() =>
            ExecSqlAsync(
                $"DELETE FROM trax.snapshot_transition WHERE draft_row_id = {instance.RowId}"
            );
        async Task Prepare()
        {
            await Clear();
            await ExecSqlAsync(
                "INSERT INTO trax.snapshot_transition (draft_row_id, revision, kind, trigger, actor, from_state, "
                    + "to_state, occurred_at) "
                    + $"SELECT {instance.RowId}, r, 'command', 'Tick', 'host', 'State' || (r % 8), "
                    + $"'State' || ((r + 1) % 8), now() - (({DeepHistory} - r) * interval '1 second') "
                    + $"FROM generate_series(1, {DeepHistory}) r"
            );
            await ExecSqlAsync("ANALYZE trax.snapshot_transition");
        }

        try
        {
            foreach (
                var (label, arguments, newest) in new[]
                {
                    ("first page", "first: 100", (long)DeepHistory),
                    (
                        "page past half of it",
                        $"first: 100, after: {DeepHistory / 2}",
                        (long)DeepHistory / 2 - 1
                    ),
                    ("oldest page", "first: 100, after: 101", 100L),
                }
            )
                await MeasureWriteAsync(
                    $"operations.machineInstance {{ transitions }} over {DeepHistory:N0} rows ({label})",
                    ListBudget,
                    prepare: async () =>
                    {
                        if (
                            await ScalarAsync<long>(
                                $"SELECT count(*) FROM trax.snapshot_transition WHERE draft_row_id = {instance.RowId}"
                            ) != DeepHistory
                        )
                            await Prepare();
                    },
                    action: async (_, ct) =>
                    {
                        var page = (
                            await OperationsFieldAsync(HistoryDocument(instance, arguments), ct)
                        ).GetProperty("transitions");
                        var items = page.GetProperty("items").EnumerateArray().ToList();
                        items[0].GetProperty("revision").GetInt64().Should().Be(newest);
                        items.Should().HaveCount(100);
                        items[^1]
                            .GetProperty("previousAt")
                            .ValueKind.Should()
                            .Be(newest == 100 ? JsonValueKind.Null : JsonValueKind.String);
                    },
                    restore: () => Task.CompletedTask
                );
        }
        finally
        {
            await Clear();
        }
    }

    // How many instances the stuck tests make overdue: a handful among millions, as a real host
    // has when one machine's timers stop being fired.
    private const int OverdueInstances = 200;

    private Task MakeOverdueAsync() =>
        ExecSqlAsync(
            "UPDATE trax.snapshot_draft SET due_at = now() - interval '1 hour' WHERE row_id IN "
                + $"(SELECT row_id FROM trax.snapshot_draft WHERE machine = '{SystemMachine}' "
                + $"ORDER BY row_id LIMIT {OverdueInstances})"
        );

    private Task ClearDueAsync() =>
        ExecSqlAsync("UPDATE trax.snapshot_draft SET due_at = NULL WHERE due_at IS NOT NULL");

    [Test]
    public async Task StuckOnly_FirstPage_ThroughTheSchema_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.machineInstances (stuck: true, through the schema)",
            ListBudget,
            prepare: MakeOverdueAsync,
            action: async (_, ct) =>
            {
                var page = await OperationsFieldAsync(
                    "{ operations { machineInstances(stuck: true, take: 100) { totalCount isCountCapped "
                        + "items { rowId machine state dueAt overdue stuck inStateSince "
                        + "waitingOn { executionId trainState workQueueEntryId } } } } }",
                    ct
                );
                page.GetProperty("totalCount").GetInt32().Should().Be(OverdueInstances);
                page.GetProperty("items")
                    .EnumerateArray()
                    .Should()
                    .OnlyContain(i =>
                        i.GetProperty("stuck").GetBoolean() && i.GetProperty("overdue").GetBoolean()
                    );
            },
            restore: ClearDueAsync
        );
    }

    [Test]
    public async Task FirstPage_WithAttention_ThroughTheSchema_WithinBudget()
    {
        // The unfiltered first page with every attention field: one instance in a hundred holds an
        // invoke token, so the page reads its runs and queue entries too.
        await MeasureAsync(
            "operations.machineInstances (attention fields, through the schema)",
            ListBudget,
            async (_, ct) =>
            {
                var page = await OperationsFieldAsync(
                    "{ operations { machineInstances(take: 500) { items { rowId hasLiveInvokedRun dueAt "
                        + "overdue stuck inStateSince waitingOn { executionId trainState workQueueEntryId } } } } }",
                    ct
                );
                page.GetProperty("items").GetArrayLength().Should().Be(500);
            }
        );
    }

    [Test]
    public async Task CountsByState_WithinBudget()
    {
        await MeasureAsync(
            "operations.machineInstanceCounts",
            CountsBudget,
            async (sp, ct) =>
            {
                var counts = await new OperationsQueries().GetMachineInstanceCounts(
                    Operations(sp),
                    ct
                );
                counts.Should().HaveCount(BulkSeeder.SnapshotMachines * BulkSeeder.SnapshotStates);
                counts.Sum(c => c.Count).Should().BeGreaterThanOrEqualTo(Profile.SnapshotDrafts);
            }
        );
    }

    [Test]
    public async Task List_ThroughTheSchema_WithinBudget_AndCarriesNoContext()
    {
        await MeasureAsync(
            "operations.machineInstances (through the schema)",
            ListBudget,
            async (_, ct) =>
            {
                var page = await OperationsFieldAsync(
                    "{ operations { machineInstances(take: 100) { totalCount isCountCapped "
                        + "items { rowId machine ownerKind id state version createdAt updatedAt hasLiveInvokedRun } } } }",
                    ct
                );
                page.GetProperty("items").GetArrayLength().Should().Be(100);
                page.GetRawText()
                    .Should()
                    .NotContain("stress-context-")
                    .And.NotContain("stress-user-");
                page.GetProperty("items")
                    .EnumerateArray()
                    .Should()
                    .OnlyContain(i => i.GetProperty("rowId").ValueKind == JsonValueKind.Number);
            }
        );
    }
}
