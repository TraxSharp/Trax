using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// An operator reads the event-storming board of a machine served from features through <c>operations.machineBoard</c>
/// (ADR <c>Trax.Docs/adr/0058</c>), the read the console's Board tab draws: anyone else is refused by the operations
/// gate, a host file with no
/// provenance gives stickies with no scenarios, and a machine not served from features has no board.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MachineBoardE2ETests
{
    private const string Database = "trax_sm_machine_board";

    private const string Query = """
        query($machine: String!) { operations { machineBoard(machine: $machine) {
          machine lanes stickies { id kind machine label scenarios { file line scenario } } links { from to } } } }
        """;

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        var (machines, host) = IrMachineViewsE2ETests.Machines();
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Instances = machines,
                Served = [typeof(WizardMachine)],
                ExposeOperations = true,
                Views = new MachineViewCatalog(host.Views),
                Services = s =>
                {
                    s.AddSingleton(host);
                    s.AddHostedService<SystemInstanceStarter>();
                },
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
    public async Task An_operator_reads_the_board_one_lane_per_machine_it_joins()
    {
        var board = await Board(E2EHost.OperatorKey, "v-order");

        board.GetProperty("machine").GetString().Should().Be("v-order");
        board.GetProperty("lanes")[0].GetString().Should().Be("v-order");
        var stickies = board.GetProperty("stickies").EnumerateArray().ToList();
        stickies
            .Should()
            .Contain(s =>
                s.GetProperty("kind").GetString() == "command"
                && s.GetProperty("label").GetString() == "Place"
            );
        stickies.Should().Contain(s => s.GetProperty("kind").GetString() == "view");
    }

    [TestCase("v-order")]
    [TestCase("v-shop")]
    public async Task An_actor_two_machines_share_does_not_join_their_lanes(string machine)
    {
        // The order's start and the shop sign's readers are both anyone signed in, one actor sticky on the whole
        // board, and no policy or view joins the two machines.
        var board = await Board(E2EHost.OperatorKey, machine);

        board
            .GetProperty("lanes")
            .EnumerateArray()
            .Select(l => l.GetString())
            .Should()
            .Equal([machine]);
        var stickies = board.GetProperty("stickies").EnumerateArray().ToList();
        stickies.Should().OnlyContain(s => s.GetProperty("machine").GetString() == machine);
        var actors = stickies
            .Where(s => s.GetProperty("kind").GetString() == "actor")
            .Select(s => s.GetProperty("id").GetString())
            .ToList();
        actors.Should().Contain("actor:anyone signed in", "the machine's own actor is still drawn");
        var links = board.GetProperty("links").EnumerateArray().ToList();
        actors
            .Should()
            .OnlyContain(
                a =>
                    links.Any(l =>
                        l.GetProperty("from").GetString() == a
                        || l.GetProperty("to").GetString() == a
                    ),
                "each actor keeps its arrow"
            );
    }

    [Test]
    public async Task A_host_file_with_no_provenance_gives_stickies_with_no_scenarios()
    {
        var board = await Board(E2EHost.OperatorKey, "v-order");

        board
            .GetProperty("stickies")
            .EnumerateArray()
            .Should()
            .OnlyContain(s => s.GetProperty("scenarios").GetArrayLength() == 0);
    }

    [Test]
    public async Task A_machine_not_served_from_features_has_no_board()
    {
        using var answer = await _host.PostAsync(
            Query,
            new { machine = WizardMachine.MachineId },
            apiKey: E2EHost.OperatorKey
        );

        answer
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineBoard")
            .ValueKind.Should()
            .Be(JsonValueKind.Null);
    }

    [Test]
    public async Task Anyone_but_an_operator_is_refused()
    {
        using var answer = await _host.PostAsync(
            Query,
            new { machine = "v-order" },
            apiKey: E2EHost.AliceKey
        );

        answer.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        // The operations gate's own refusal, as for every operations read.
        errors[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_AUTHORIZATION");
    }

    private async Task<JsonElement> Board(string key, string machine)
    {
        using var answer = await _host.PostAsync(Query, new { machine }, apiKey: key);
        answer
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(answer.RootElement.ToString());
        return answer
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineBoard")
            .Clone();
    }
}
