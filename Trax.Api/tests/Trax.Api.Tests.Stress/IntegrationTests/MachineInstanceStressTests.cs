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
