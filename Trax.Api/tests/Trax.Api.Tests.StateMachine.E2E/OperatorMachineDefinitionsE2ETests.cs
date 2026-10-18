using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// <c>operations.machines</c>: every machine a host registers, with no instance needed, and its graph as
/// <c>trax machine show</c> draws it, behind the operations gate.
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
[NonParallelizable]
public class OperatorMachineDefinitionsE2ETests
{
    private const string Database = "trax_sm_operator_definitions";

    private const string Machines = """
        { operations {
            machineInstanceCounts { machine count }
            machines {
              name version systemOwned schemaHash initialState graphUnavailableReason
              states { name isInitial isFinal isCommitted invokesTrain spawnsMachine }
              transitions { from command to kind guard effect after }
              timers { state command target after field min max }
            }
        } }
        """;

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions { Served = [typeof(WizardMachine)], ExposeOperations = true }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        await ShelfHosts.StopAsync(_host);
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_registered_machine_with_no_instances_is_listed_with_the_graph_trax_machine_show_draws()
    {
        using var doc = await _host.PostAsync(Machines, apiKey: E2EHost.OperatorKey);
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        var operations = doc.RootElement.GetProperty("data").GetProperty("operations");
        operations
            .GetProperty("machineInstanceCounts")
            .GetArrayLength()
            .Should()
            .Be(0, "no instance exists, so the counts list no machine");

        var machines = operations.GetProperty("machines").EnumerateArray().ToList();
        machines
            .Select(m => m.GetProperty("name").GetString())
            .Should()
            .Contain([ShelfMachine.MachineId, WizardMachine.MachineId])
            .And.BeInAscendingOrder(StringComparer.Ordinal);

        foreach (var name in new[] { ShelfMachine.MachineId, WizardMachine.MachineId })
        {
            var served = machines.Single(m => m.GetProperty("name").GetString() == name);
            var registered = _host.Services.GetServices<IMachine>().Single(m => m.Name == name);
            var drawn = MachineGraph.Parse(registered.ExportIr());

            served.GetProperty("initialState").GetString().Should().Be(drawn.Initial);
            served.GetProperty("schemaHash").GetString().Should().Be(registered.SchemaHash);
            served.GetProperty("graphUnavailableReason").ValueKind.Should().Be(JsonValueKind.Null);
            served
                .GetProperty("states")
                .EnumerateArray()
                .Select(s => s.GetProperty("name").GetString())
                .Should()
                .Equal(drawn.States, $"{name} lists the states trax machine show prints");
            served
                .GetProperty("transitions")
                .EnumerateArray()
                .Select(Edge)
                .Should()
                .Equal(
                    drawn.Transitions.Select(t =>
                        $"{t.From} -{t.Trigger}-> {t.To} [{t.Kind.ToString().ToUpperInvariant()}] "
                        + $"guard={t.Guard} effect={t.Effect} after={t.Timer}"
                    ),
                    $"{name} lists the edges trax machine show prints, in its order"
                );
        }

        var wizard = machines.Single(m =>
            m.GetProperty("name").GetString() == WizardMachine.MachineId
        );
        wizard.GetProperty("version").GetInt32().Should().Be(1);
        wizard.GetProperty("systemOwned").GetBoolean().Should().BeFalse();
        var states = wizard
            .GetProperty("states")
            .EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!);
        states[nameof(WizardState.Naming)].GetProperty("isInitial").GetBoolean().Should().BeTrue();
        states[nameof(WizardState.Building)]
            .GetProperty("invokesTrain")
            .GetString()
            .Should()
            .Be(typeof(IBuildWizardTrain).FullName);
        states[nameof(WizardState.Approving)]
            .GetProperty("invokesTrain")
            .ValueKind.Should()
            .Be(JsonValueKind.Null);

        var edges = wizard.GetProperty("transitions").EnumerateArray().Select(Edge).ToList();
        edges
            .Should()
            .Contain(
                $"Naming -Describe-> Paying [COMMAND] guard={WizardMachine.NeedsPages} effect= after="
            )
            .And.Contain("Paying -Pay-> Approving [COMMAND] guard= effect=wizard:pay after=")
            .And.Contain(
                $"Approving -Approving.after-> Expired [TIMER] guard= effect= after=PT1H",
                "the timer is an edge of its own, as trax machine show draws it"
            )
            .And.Contain("Building -Building.done-> Built [OUTCOME] guard= effect= after=")
            .And.Contain("Building -Building.failed-> BuildFailed [OUTCOME] guard= effect= after=");

        var timer = wizard.GetProperty("timers").EnumerateArray().Single();
        timer.GetProperty("state").GetString().Should().Be(nameof(WizardState.Approving));
        timer.GetProperty("target").GetString().Should().Be(nameof(WizardState.Expired));
        timer.GetProperty("after").GetString().Should().Be("PT1H");
    }

    [Test]
    public async Task A_caller_outside_the_operations_gate_is_not_shown_the_machines()
    {
        foreach (var caller in new[] { E2EHost.BobKey, null })
        {
            using var doc = await _host.PostAsync(
                "{ operations { machines { name } } }",
                apiKey: caller
            );
            doc.RootElement.TryGetProperty("errors", out _)
                .Should()
                .BeTrue($"{caller ?? "an anonymous caller"} is outside the operations gate");
            doc.RootElement.GetRawText().Should().NotContain(WizardMachine.MachineId);
        }
    }

    private static string Edge(JsonElement t) =>
        $"{t.GetProperty("from").GetString()} -{t.GetProperty("command").GetString()}-> "
        + $"{t.GetProperty("to").GetString()} [{t.GetProperty("kind").GetString()}] "
        + $"guard={t.GetProperty("guard").GetString()} effect={t.GetProperty("effect").GetString()} "
        + $"after={t.GetProperty("after").GetString()}";
}
