using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A machine served from its IR file and its host entry, with no C# class, runs over GraphQL as a hand-written one
/// does (ADR <c>Trax.Docs/adr/0058</c>): a client starts and moves it through its typed operations, its input is read
/// into the record emitted for it, and every operation is held to the host entry's rules.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IrMachineE2ETests
{
    private const string Database = "trax_sm_machine_from_ir";

    private const string Approver = WizardMachine.ApproverRole;

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);

        var ir = JsonNode.Parse(File.ReadAllText(IrFile("turnstile")))!.AsObject();
        ir["id"] = "ir-turnstile";
        var approvers = new AccessRule([
            [Approver],
        ]);
        var entry = new HostMachine(
            "ir-turnstile",
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
            ),
            null,
            approvers,
            approvers,
            new Dictionary<string, TriggerAccess>
            {
                ["Coin"] = new(approvers),
                ["Push"] = new(AccessRule.AnyAuthenticated),
            },
            new Dictionary<string, HostInvoke>()
        );
        var machines = IrMachines.Create([new IrMachines.Source(ir, entry)], []);

        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions { Instances = machines }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task Its_operations_run_under_the_host_entrys_rules()
    {
        (await Start("s0", E2EHost.BobKey))
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("FORBIDDEN", "only an approver may start one");

        var id = (await Start("s1", E2EHost.AliceKey)).GetProperty("id").GetString()!;

        (await Event("coin", id, "c0", E2EHost.BobKey, """input: { coin: "quarter" }, """))
            .GetProperty("problem")
            .ValueKind.Should()
            .NotBe(JsonValueKind.Null, "only the owner moves their instance");

        var coined = await Event(
            "coin",
            id,
            "c1",
            E2EHost.AliceKey,
            """input: { coin: "quarter" }, """
        );
        coined
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("Unlocked", $"the approver's coin moves it: {coined}");

        var pushed = await Event("push", id, "p1", E2EHost.AliceKey, "");
        pushed
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("Locked", $"Push admits any signed-in caller: {pushed}");
    }

    [Test]
    public async Task An_input_that_is_not_the_records_is_refused()
    {
        var id = (await Start("s2", E2EHost.AliceKey)).GetProperty("id").GetString()!;

        using var doc = await _host.PostAsync(
            $$"""mutation { machines { irTurnstile { coin(id: "{{id}}", input: { coin: 25 }, requestId: "c2") { state } } } }""",
            apiKey: E2EHost.AliceKey
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeTrue("the coin is a string in the IR and in the record emitted for it");
    }

    private async Task<JsonElement> Start(string requestId, string key)
    {
        using var doc = await _host.PostAsync(
            $$"""mutation { machines { irTurnstile { start(requestId: "{{requestId}}") { id problem { code } } } } }""",
            apiKey: key
        );
        return Data(doc, "start");
    }

    private async Task<JsonElement> Event(
        string field,
        string id,
        string requestId,
        string key,
        string input
    )
    {
        using var doc = await _host.PostAsync(
            $$"""mutation { machines { irTurnstile { {{field}}(id: "{{id}}", {{input}}requestId: "{{requestId}}") { state problem { code } } } } }""",
            apiKey: key
        );
        return Data(doc, field);
    }

    private static JsonElement Data(JsonDocument doc, string field) =>
        doc.RootElement.TryGetProperty("data", out var data)
            ? data.GetProperty("machines").GetProperty("irTurnstile").GetProperty(field).Clone()
            : throw new InvalidOperationException(doc.RootElement.GetRawText());

    private static string IrFile(string name)
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var file = Path.Combine(
                dir.FullName,
                "Trax.StateMachine",
                "conformance",
                "machines",
                name,
                $"{name}.ir.json"
            );
            if (File.Exists(file))
                return file;
        }
        throw new FileNotFoundException(
            $"Trax.StateMachine/conformance/machines/{name}/{name}.ir.json"
        );
    }
}
