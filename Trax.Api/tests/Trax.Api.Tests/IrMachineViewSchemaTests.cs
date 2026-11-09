using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.Tests;

/// <summary>
/// A view's fields are fields of its row type beside the instance's <c>id</c> (ADR <c>Trax.Docs/adr/0058</c>), so a
/// field named <c>id</c>, or one GraphQL cannot name, refuses the host naming the view and the field, rather than
/// replacing the instance's id or failing the schema build with HotChocolate's own error.
/// </summary>
[TestFixture]
public class IrMachineViewSchemaTests
{
    [Test]
    public async Task A_view_whose_fields_GraphQL_can_name_is_served()
    {
        (await RefusalAsync("status")).Should().BeNull();
    }

    [TestCase("id")]
    [TestCase("coins taken")]
    [TestCase("__coins")]
    public async Task A_view_field_its_row_cannot_carry_refuses_the_host_naming_it(string field)
    {
        var refusal = await RefusalAsync(field);

        refusal.Should().NotBeNull();
        refusal!.ToString().Should().Contain("'turnstile'").And.Contain($"'{field}'");
    }

    [TestCase("shop's sign")]
    [TestCase("café")]
    [TestCase("2 turnstiles")]
    public async Task A_view_GraphQL_cannot_name_refuses_the_host_naming_it(string name)
    {
        var refusal = await RefusalAsync("status", name);

        refusal.Should().NotBeNull();
        refusal!.ToString().Should().Contain($"'{name}'").And.Contain("has no GraphQL name");
    }

    [Test]
    public async Task Two_views_that_are_one_GraphQL_name_refuse_the_host_naming_both()
    {
        var refusal = await RefusalAsync("status", "the turnstile", "the-turnstile");

        refusal.Should().NotBeNull();
        refusal!
            .ToString()
            .Should()
            .Contain("'the turnstile'")
            .And.Contain("'the-turnstile'")
            .And.Contain("TheTurnstile");
    }

    // A host serving the turnstile from its IR and the given views, each its state under the field named field.
    private static Task<Exception?> RefusalAsync(string field, params string[] names)
    {
        if (names.Length == 0)
            names = ["turnstile"];
        var ir = JsonNode
            .Parse(File.ReadAllText(IrMachineSchemaTests.IrFile("turnstile")))!
            .AsObject();
        ir["id"] = "ir-turnstile";
        var host = new HostMachine(
            "ir-turnstile",
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
            ),
            null,
            AccessRule.AnyAuthenticated,
            AccessRule.AnyAuthenticated,
            new Dictionary<string, TriggerAccess>
            {
                ["Coin"] = new(AccessRule.AnyAuthenticated),
                ["Push"] = new(AccessRule.AnyAuthenticated),
            },
            new Dictionary<string, HostInvoke>()
        );
        var machines = IrMachines.Create([new IrMachines.Source(ir, host)], []);
        var views = names.ToDictionary(
            name => name,
            name => new HostView(
                name,
                [new ViewRead("ir-turnstile", null)],
                [new ViewField(field, "ir-turnstile", ViewField.State, null)],
                AccessRule.AnyAuthenticated
            )
        );
        return MachineSchemaHost.RefusalAsync(
            [],
            services: s =>
            {
                s.AddIrMachines(machines);
                s.AddSingleton(new MachineViewCatalog(views));
            }
        );
    }
}
