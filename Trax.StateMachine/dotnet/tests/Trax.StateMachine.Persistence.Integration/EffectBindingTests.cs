using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A machine served from its features runs the C# its host file names (ADR <c>Trax.Docs/adr/0058</c>): Pay's run-once
/// charge, found by its interface's full name and resolved from the host's services, and the custom reduction and rule
/// that price the cart. A send is the client's whole part in Pay: it gives no input, and the server charges once.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class EffectBindingTests(StoreProvider provider)
{
    private InstanceHost _host = null!;
    private CountingCharge _charge = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _charge = new CountingCharge();
        var machine = FeatureCheckout.Served();
        _host = InstanceHost.Create(
            provider,
            services: s =>
            {
                s.AddIrMachines([machine]);
                s.AddSingleton<IFeatureCharge>(_charge);
            }
        );
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Ada_sends_Pay_twice_with_one_request_id_and_is_charged_once()
    {
        var ada = DeskRoles.Client($"ada-{Guid.NewGuid():N}");
        var before = _charge.Calls;
        using var scope = _host.Scope();
        var service = _host.Service(scope, "feature-checkout");
        var id = (await service.Start(ada, new JsonObject(), requestId: "s1"))
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject.Id;
        var added = (
            await service.Advance(
                ada,
                id,
                "AddItem",
                new JsonObject { ["item"] = "bread" },
                requestId: "a1"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;
        added.Snapshot.Context["total"]!
            .ToJsonString()
            .Should()
            .Be($"{FeatureCheckout.UnitPrice}", "the host's reduction prices the cart");
        (await service.Advance(ada, id, "Next", requestId: "a2"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        var first = await service.Send(ada, id, "pay-1");
        var second = await service.Send(ada, id, "pay-1");

        _charge.Calls.Should().Be(before + 1, "a run-once effect runs once");
        var paid = first.Should().BeOfType<AdvanceOutcome.Advanced>().Subject.Snapshot;
        paid.State.Should().Be("Paid");
        paid.Context["receipt"]!.GetValue<string>().Should().StartWith("charge-");
        var again = second.Should().BeOfType<AdvanceOutcome.Advanced>().Subject.Snapshot;
        again.State.Should().Be(paid.State);
        JsonNode
            .DeepEquals(again.Context, paid.Context)
            .Should()
            .BeTrue("the same request answers the same Paid snapshot");
    }

    [Test]
    public async Task A_client_cannot_fire_the_effects_trigger_itself()
    {
        var ada = DeskRoles.Client($"ada-{Guid.NewGuid():N}");
        using var scope = _host.Scope();
        var service = _host.Service(scope, "feature-checkout");
        var id = (await service.Start(ada, new JsonObject(), requestId: "s1"))
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject.Id;
        await service.Advance(ada, id, "AddItem", new JsonObject { ["item"] = "bread" }, "a1");
        await service.Advance(ada, id, "Next", requestId: "a2");

        var fired = await service.Advance(
            ada,
            id,
            "Pay",
            new JsonObject { ["receipt"] = "forged" },
            requestId: "a3"
        );

        fired
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>("only a send runs the charge and fires Pay");
    }

    [Test]
    public void A_host_file_whose_effect_type_is_not_an_effect_interface_is_refused()
    {
        var served = FeatureCheckout.Served();
        var ir = JsonNode.Parse(served.ExportIr())!.AsObject();
        var host = new HostMachine(
            "feature-checkout",
            IrMachines.Hash(ir),
            null,
            AccessRule.AnyAuthenticated,
            AccessRule.AnyAuthenticated,
            new Dictionary<string, TriggerAccess>(),
            new Dictionary<string, HostInvoke>()
        )
        {
            Effects = new Dictionary<string, string> { ["Pay"] = typeof(CountingCharge).FullName! },
            Rules = new Dictionary<string, string> { ["priced"] = typeof(TotalIsPriced).FullName! },
            Reductions = new Dictionary<string, string>
            {
                ["add-item"] = typeof(AddPricedItem).FullName!,
            },
            Migrations = new Dictionary<int, string> { [1] = typeof(BackfillTotal).FullName! },
        };

        var act = () =>
            IrMachines.Create(new IrMachines.Source(ir, host), [typeof(CountingCharge).Assembly]);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CountingCharge*not an interface extending ISnapshotEffect*");
    }
}
