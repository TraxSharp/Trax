using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The host file is read strictly (<c>Trax.Docs/adr/0058</c>). It holds every access rule of the machines derived
/// from features, so a key it does not know is refused rather than dropped, a trigger with no entry is not served,
/// and a trigger a policy fires is the server's alone.
/// </summary>
public class HostFileFormatTests
{
    [Test]
    public void The_bakery_host_file_reads_into_its_machines_policies_and_views()
    {
        var host = HostFile.Read(Bakery().ToJsonString());

        var order = host.Machines["order"];
        order.SystemKey.Should().BeNull();
        order.Start!.RoleSets.Should().ContainSingle().Which.Should().Equal("customer");
        order.Triggers["Bake"].IsServer.Should().BeTrue();
        order
            .Invokes["Paying"]
            .Input.Should()
            .ContainKey("amount")
            .WhoseValue.Should()
            .Be("loaves");
        host.Machines["shop"].SystemKey.Should().Be("shop/main");
        host.Policies.Select(p => p.Shape).Should().Equal("toSystem", "broadcast", "linked");
        host.Views["the shop"].Readers.IsAnyAuthenticated.Should().BeTrue();
        host.Provenance["order"]["edge Draft Place"]
            .Should()
            .ContainSingle()
            .Which.Line.Should()
            .Be(12);
    }

    [Test]
    public void A_key_it_does_not_know_is_refused_naming_it()
    {
        var host = Bakery();
        host["machines"]!["order"]!["requiresPolicy"] = "CanOrder";

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("host.json /machines/order/requiresPolicy:*");
    }

    [Test]
    public void An_owner_named_by_actor_is_refused()
    {
        var host = Bakery();
        host["machines"]!["order"]!["owner"] = new JsonObject { ["user"] = "customer" };

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("*/machines/order/owner/user:*not a key*");
    }

    [Test]
    public void A_policy_firing_a_trigger_clients_may_fire_is_refused()
    {
        var host = Bakery();
        host["policies"]![1]!["then"]!["trigger"] = "Place";

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("*/policies/1/then/trigger:*client's trigger*");
    }

    [Test]
    public void A_client_rule_on_a_system_machine_is_refused()
    {
        var host = Bakery();
        host["machines"]!["shop"]!["triggers"]!["Open"] = new JsonObject
        {
            ["roles"] = new JsonArray(new JsonArray("baker")),
        };

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("*/machines/shop/triggers/Open:*server only*");
    }

    [Test]
    public void A_policy_listed_twice_is_refused_at_the_second()
    {
        var host = Bakery();
        var twice = host["policies"]![1]!.DeepClone();
        twice["then"]!["inState"] = "Draft";
        host["policies"]!.AsArray().Add(twice);

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("*/policies/3:*shop.Open>order.Bake*policies/1*");
    }

    [Test]
    public void A_batch_size_is_refused_since_it_is_the_hosts_option()
    {
        var host = Bakery();
        host["policies"]![1]!["batch"] = 100;

        var read = () => HostFile.Read(host.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/policies/1/batch:*not a key*");
    }

    private static JsonObject Bakery() =>
        JsonNode
            .Parse(
                File.ReadAllText(
                    Path.Combine(
                        FixturePaths.ConformanceRoot,
                        "formats",
                        "host",
                        "valid",
                        "bakery.json"
                    )
                )
            )!
            .AsObject();
}
