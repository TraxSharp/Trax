using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Index;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The <c>source-partition</c> machine: discovery starts one system-owned instance per index partition,
/// each ingests its partition through the train its <c>Ingesting</c> state invokes, an unsure ingest is
/// routed to review by a guarded outcome, and a failed one is retried by entering <c>Ingesting</c> again.
/// Operators see the instances read-only.
/// </summary>
[TestFixture]
public class SourcePartitionMachineTests : RecoveryTestFixture
{
    private static readonly (string Source, string Month)[] Partitions =
    [
        .. IndexFixture.Sources.SelectMany(s => IndexFixture.Months.Select(m => (s, m))),
    ];

    private DateTime _started;

    [SetUp]
    public async Task StartFromNoInstances()
    {
        await PartitionMachines.ResetAsync();
        _started = DateTime.UtcNow.AddSeconds(-1);
    }

    [Test]
    public async Task Discovery_StartsOneInstancePerPartition_AndARerunStartsNoneAndQueuesNothing()
    {
        var first = await DiscoverAsync();
        first.GetProperty("instancesStarted").GetInt32().Should().Be(Partitions.Length);
        first.GetProperty("instancesSentToIngest").GetInt32().Should().Be(Partitions.Length);
        var queued = await IngestRunsAsync();
        queued
            .Should()
            .Be(Partitions.Length, "each instance's entry into Ingesting queues one run");

        var second = await DiscoverAsync();

        second.GetProperty("instancesStarted").GetInt32().Should().Be(0);
        second.GetProperty("instancesSentToIngest").GetInt32().Should().Be(0);
        (await PartitionMachines.AllAsync()).Should().HaveCount(Partitions.Length);
        (await IngestRunsAsync()).Should().Be(queued, "a rerun of discovery queues nothing new");
    }

    [Test]
    public async Task EachPartition_IsIngested_AndTheUnsureOne_GoesToReview()
    {
        await DiscoverAsync();

        foreach (var (source, month) in Partitions)
        {
            var unsure = source == IndexFixture.Crossref && month == "2025-02";
            var row = await PartitionMachines.WaitForStateAsync(
                source,
                month,
                unsure ? nameof(PartitionState.NeedsReview) : nameof(PartitionState.Ingested)
            );

            var context = JsonNode.Parse(row.Context)!.AsObject();
            context["source"]!.GetValue<string>().Should().Be(source);
            context["month"]!.GetValue<string>().Should().Be(month);
            context["works"]!.GetValue<int>().Should().Be(3);
            context["fingerprint"]!.GetValue<string>().Should().HaveLength(64);
            context["needsReview"]!.GetValue<int>().Should().Be(unsure ? 1 : 0);
            context.Should().HaveCount(7, "the instance holds counts and a fingerprint, no rows");
        }

        // A person approves the partition held for review; the sample's operator mutation fires it.
        (await ActAsync(IndexFixture.Crossref, "2025-02", "APPROVE"))
            .Should()
            .Be((nameof(PartitionState.Approved), (string?)null));
        // An ingested partition has nothing to retry.
        (await ActAsync(IndexFixture.OpenAlex, "2025-01", "RETRY"))
            .Should()
            .Be(((string?)null, "no-transition"));
    }

    [Test]
    public async Task ACrashedIngest_Fails_AndARetry_RunsItAgainUnderANewToken()
    {
        const string source = IndexFixture.OpenAlex;
        const string month = "2025-03";
        Faults.Arm(IndexFixture.PartitionKey(source, month), CrashPoint.Ingest);
        try
        {
            await DiscoverAsync(source);

            await PartitionMachines.WaitForStateAsync(source, month, nameof(PartitionState.Failed));
            var instance = (await PartitionMachines.OfAsync(source, month))!;
            var failedRun = (await RunsOfAsync(instance.Id)).Last();
            (await TrainStateOfAsync(failedRun.MetadataId))
                .Should()
                .Be(TrainState.Failed, "the scheduler never retries an invoked run");

            (await ActAsync(source, month, "RETRY"))
                .Should()
                .Be((nameof(PartitionState.Ingesting), (string?)null));
            var retried = (await PartitionMachines.OfAsync(source, month))!;
            var retryRun = (await RunsOfAsync(instance.Id)).Last();
            retryRun.ExternalId.Should().NotBe(failedRun.ExternalId, "a retry is a new run");
            retried.InvokeToken.Should().Be(retryRun.ExternalId);

            await PartitionMachines.WaitForStateAsync(
                source,
                month,
                nameof(PartitionState.Ingested)
            );

            // The failed run's outcome was applied once, under its own token; that token is gone, so it
            // can never move the instance again, however late a delivery of it comes. Make that late
            // delivery now, through the reconciler's own delivery, rather than wait for a sweep that
            // may or may not have run on a slow machine.
            var outcomes =
                SharedRecoverySetup.Factory.Services.GetRequiredService<IInvokedRunOutcomes>();
            (await outcomes.Deliver(failedRun.ExternalId))
                .Should()
                .BeNull("the failed run's token was cleared when its outcome was applied");
            (await outcomes.Deliver(retryRun.ExternalId))
                .Should()
                .BeNull(
                    "the retry's outcome was applied already, so a second delivery moves nothing"
                );
            (await PartitionMachines.OfAsync(source, month))!
                .State.Should()
                .Be(nameof(PartitionState.Ingested));
        }
        finally
        {
            Faults.Disarm(IndexFixture.PartitionKey(source, month));
        }
    }

    [Test]
    public async Task Operators_SeeThePartitions_ReadOnly_AndNoUserReachesThem()
    {
        await DiscoverAsync(IndexFixture.Crossref);
        await PartitionMachines.WaitUntilSettledAsync();

        var response = await GraphQL.SendAsync(
            """
            { operations { machineInstances(machine: "source-partition", ownerKind: SYSTEM, take: 50) {
                totalCount items { machine ownerKind id state hasLiveInvokedRun } } } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var page = response.GetData("operations", "machineInstances");
        page.GetProperty("totalCount").GetInt32().Should().Be(IndexFixture.Months.Count);
        var states = page.GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("state").GetString())
            .ToList();
        states.Should().Contain(nameof(PartitionState.NeedsReview));
        states.Should().OnlyContain(s => s == "Ingested" || s == "NeedsReview");

        // No operator field carries a snapshot's context.
        var fields = await GraphQL.SendAsync(
            """{ __type(name: "MachineInstance") { fields { name } } }""",
            OperatorKey
        );
        fields
            .GetData("__type", "fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .Should()
            .NotContain("context");

        // A user's draft operations never reach a system instance, even by its id.
        var id = (await PartitionMachines.OfAsync(IndexFixture.Crossref, "2025-01"))!.Id;
        var load = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { stateMachine { loadSnapshot(input: {
              machine: "source-partition", id: "{{id}}" }) { output { snapshot problem { code } } } } } }
            """,
            OperatorKey
        );
        load.HasErrors.Should().BeFalse(load.FirstErrorMessage);
        var output = load.GetData("dispatch", "stateMachine", "loadSnapshot", "output");
        output.GetProperty("snapshot").ValueKind.Should().Be(JsonValueKind.Null);
        output
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("unknown-machine");
    }

    [Test]
    public async Task OnlyAnOperator_MayActOnAPartition()
    {
        await DiscoverAsync(IndexFixture.Crossref);
        await PartitionMachines.WaitUntilSettledAsync();
        var before = (await PartitionMachines.OfAsync(IndexFixture.Crossref, "2025-02"))!;
        before.State.Should().Be(nameof(PartitionState.NeedsReview));
        var runsBefore = (await RunsOfAsync(before.Id)).Count;

        var response = await GraphQL.SendAsync(
            """
            mutation { dispatch { partitionAction(input: { source: "Crossref", month: "2025-02", action: APPROVE }) {
              output { state problem } } } }
            """,
            Auth.DemoKeys.Viewer
        );

        response.HasErrors.Should().BeTrue("the viewer key holds no Operator role");
        response
            .Root.GetProperty("errors")[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_AUTHORIZATION", response.FirstErrorMessage);
        var after = (await PartitionMachines.OfAsync(IndexFixture.Crossref, "2025-02"))!;
        after.State.Should().Be(before.State, "a refused action moves nothing");
        after.Version.Should().Be(before.Version);
        (await RunsOfAsync(before.Id)).Should().HaveCount(runsBefore, "and queues nothing");
    }

    private static FaultInjector Faults =>
        SharedRecoverySetup.Factory.Services.GetRequiredService<FaultInjector>();

    private async Task<JsonElement> DiscoverAsync(string? source = null)
    {
        var filter = source is null ? "{}" : $$"""{ source: "{{source}}" }""";
        var response = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { discoverPartitions(input: {{filter}}) {
              output { instancesStarted instancesSentToIngest partitions { source month } } } } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("dispatch", "discoverPartitions", "output");
    }

    private async Task<(string? State, string? Problem)> ActAsync(
        string source,
        string month,
        string action
    )
    {
        var response = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { partitionAction(input: { source: "{{source}}", month: "{{month}}", action: {{action}} }) {
              output { state problem } } } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var output = response.GetData("dispatch", "partitionAction", "output");
        return (output.GetProperty("state").GetString(), output.GetProperty("problem").GetString());
    }

    // Every ingest run a partition instance has queued, whenever it was queued.
    private async Task<int> IngestRunsAsync()
    {
        var ids = (await PartitionMachines.AllAsync()).Select(x => x.Id).ToList();
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .CountAsync(w =>
                w.InvokingMachine == SourcePartitionMachine.MachineId
                && w.InvokingInstanceId != null
                && ids.Contains(w.InvokingInstanceId.Value)
                && w.CreatedAt >= _started
            );
    }

    private static async Task<List<Trax.Effect.Models.WorkQueue.WorkQueue>> RunsOfAsync(Guid id)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(w => w.InvokingInstanceId == id)
            .OrderBy(w => w.Id)
            .ToListAsync();
    }

    private static async Task<TrainState?> TrainStateOfAsync(long? metadataId)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => (TrainState?)m.TrainState)
            .SingleOrDefaultAsync();
    }
}
