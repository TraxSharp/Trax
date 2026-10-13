using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Under <c>SaveTrainParameters()</c> a machine operation's run row is kept for audit without the instance's
/// context: the internal trains store no output, and the generic save stores its snapshot masked. A plain train's
/// output is still stored, and the operator's execution detail reads a machine run with no output.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineRunRecordE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_run_record";

    private static readonly string[] MachineTrains =
    [
        typeof(IStartSnapshot).FullName!,
        typeof(IAdvanceSnapshot).FullName!,
        typeof(ISendSnapshot).FullName!,
        typeof(ISaveSnapshot).FullName!,
        typeof(ILoadSnapshot).FullName!,
        typeof(IDiscardSnapshot).FullName!,
    ];

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                Machines = E2EHostOptions.OptInToGeneric,
                Services = E2EHostOptions.AddGenericPolicy,
                ExposeOperations = true,
                SaveTrainParameters = true,
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_machine_event_run_row_stores_no_output_and_a_plain_train_still_does()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey, title: "Atlas");
        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput(title: "Atlas", pages: 4)
        );
        var paid = await _host.AdvanceWizardAsync(E2EHost.AliceKey, "pay", started.Id);
        var receipt = paid.Context.GetProperty("receipt").GetString()!;
        receipt.Should().StartWith("receipt-");

        (await MachineRuns())
            .Should()
            .BeGreaterThanOrEqualTo(3, "start, describe and pay each ran an internal train");
        (await MachineRunsWithOutput())
            .Should()
            .Be(
                0,
                "a machine train's output is the instance's snapshot, which no stored copy carries, per "
                    + Adr
            );
        (await MachineRunsMentioning(receipt))
            .Should()
            .Be(0, "the receipt lives only in the context, so no run row may carry it");

        using var ping = await _host.PostAsync(
            """{ discover { ping(input: { value: "plain-output" }) { value } } }""",
            apiKey: E2EHost.AliceKey
        );
        WizardOps.ErrorCodes(ping).Should().BeEmpty(ping.RootElement.GetRawText());
        (
            await E2ESql.ScalarAsync<string>(
                Database,
                "SELECT output::text FROM trax.metadata WHERE name = @name ORDER BY id DESC LIMIT 1",
                ("name", typeof(IPing).FullName!)
            )
        )
            .Should()
            .Contain("plain-output", "a train that does not withhold its output keeps it stored");
    }

    [Test]
    public async Task The_generic_save_stores_its_snapshot_masked_and_no_output()
    {
        const string marker = "save-context-marker";
        var id = Guid.NewGuid();
        var snapshot =
            """{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"MARKER"}}""".Replace(
                "MARKER",
                marker
            );

        using var saved = await _host.PostAsync(
            """
            mutation Save($input: SaveSnapshotInput!) {
              dispatch { stateMachine { saveSnapshot(input: $input) { output { snapshot problem { code } } } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = id.ToString(),
                    snapshot,
                },
            },
            E2EHost.AliceKey
        );
        var output = saved
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("stateMachine")
            .GetProperty("saveSnapshot")
            .GetProperty("output");
        output
            .GetProperty("problem")
            .ValueKind.Should()
            .Be(JsonValueKind.Null, saved.RootElement.GetRawText());
        output
            .GetProperty("snapshot")
            .GetString()
            .Should()
            .Contain(marker, "the caller still gets the stored snapshot back");

        var input = await E2ESql.ScalarAsync<string>(
            Database,
            "SELECT input::text FROM trax.metadata WHERE name = @name AND input::text LIKE @id ORDER BY id DESC LIMIT 1",
            ("name", typeof(ISaveSnapshot).FullName!),
            ("id", $"%{id}%")
        );
        input.Should().NotBeNull("the save's run row records its input");
        input
            .Should()
            .NotContain(marker, "the save's snapshot carries the context, so it is stored masked")
            .And.Contain("_redacted");
        (await MachineRunsMentioning(marker))
            .Should()
            .Be(0, "neither the save's input nor its output keeps the context, per " + Adr);
    }

    [Test]
    public async Task The_operators_execution_detail_reads_a_machine_run_with_no_output()
    {
        await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var run = await E2ESql.ScalarAsync<long>(
            Database,
            "SELECT id FROM trax.metadata WHERE name = @name ORDER BY id DESC LIMIT 1",
            ("name", typeof(IAdvanceSnapshot).FullName!)
        );

        using var detail = await _host.PostAsync(
            $$"""{ operations { executionDetail(id: {{run}}) { id name input output } } }""",
            apiKey: E2EHost.OperatorKey
        );

        WizardOps.ErrorCodes(detail).Should().BeEmpty(detail.RootElement.GetRawText());
        var execution = detail
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("executionDetail");
        execution.GetProperty("id").GetInt64().Should().Be(run);
        execution.GetProperty("output").ValueKind.Should().Be(JsonValueKind.Null);
        execution.GetProperty("input").ValueKind.Should().Be(JsonValueKind.String);
    }

    private Task<long> MachineRuns() =>
        E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM trax.metadata WHERE name = ANY(@names)",
            ("names", MachineTrains)
        );

    private Task<long> MachineRunsWithOutput() =>
        E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM trax.metadata WHERE name = ANY(@names) AND output IS NOT NULL",
            ("names", MachineTrains)
        );

    private Task<long> MachineRunsMentioning(string value) =>
        E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM trax.metadata WHERE name = ANY(@names) "
                + "AND (coalesce(input::text, '') LIKE @value OR coalesce(output::text, '') LIKE @value)",
            ("names", MachineTrains),
            ("value", $"%{value}%")
        );
}
