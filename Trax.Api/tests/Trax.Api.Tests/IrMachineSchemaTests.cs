using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.Extensions.Hosting;
using Trax.StateMachine;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.Tests;

/// <summary>
/// A machine served from its IR file and its host entry, with no C# class, is served over GraphQL as a hand-written one
/// is (ADR <c>Trax.Docs/adr/0058</c>): the records emitted for it type its operations. Running them is
/// <c>IrMachineE2ETests</c>', in Trax.Api.Tests.StateMachine.E2E, whose host runs the mediator.
/// </summary>
[TestFixture]
public class IrMachineSchemaTests
{
    private const string Approver = "Approver";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        var ir = JsonNode.Parse(File.ReadAllText(IrFile("turnstile")))!.AsObject();
        ir["id"] = "ir-turnstile";
        var approvers = new AccessRule([
            [Approver],
        ]);
        var host = new HostMachine(
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
        var machines = IrMachines.Create([new IrMachines.Source(ir, host)], []);
        _host = await MachineSchemaHost.StartAsync([], services: s => s.AddIrMachines(machines));
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Its_operations_are_in_the_schema_typed_from_the_emitted_records()
    {
        var executor = await MachineSchemaHost.ExecutorAsync(_host);

        var mutations = MachineSchemaHost.MachineType(
            executor,
            MachineSchemaHost.RootMutation,
            "irTurnstile"
        );
        mutations.Should().NotBeNull("a user's machine from its IR is served");
        mutations!.Fields.Select(f => f.Name).Should().Contain(["start", "coin", "push"]);
        var coin = mutations.Fields["coin"].Arguments["input"].Type.NamedType();
        ((InputObjectType)coin).Fields.Select(f => f.Name).Should().Contain("coin");
    }

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
