using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A Then that moves another instance after a When is a policy (ADR <c>Trax.Docs/adr/0058</c>): the server fires a
/// trigger only it fires, of a shape the owners decide. A user's machine reaches another of the same owner's through a
/// link in the glossary, a system machine reaches every waiting instance, and anything reaches a system machine at its
/// key. What a policy cannot be (another owner's instance, no link, a trigger a person also fires) is a problem.
/// </summary>
public class PolicyDerivationTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed", "Expired": "has expired", "Baking": "is baking" },
              "triggers": {
                "Place": { "phrase": "places {subject}" },
                "Expire": { "phrase": "lets {subject} expire" },
                "Bake": { "phrase": "{subject} starts baking" }
              }
            },
            "receipt": {
              "ownedBy": "customer",
              "noun": "receipt",
              "start": "has a receipt",
              "initial": "Issued",
              "context": [ { "name": "orderId", "type": "string", "nullable": true, "format": "uuid", "constraints": [] } ],
              "states": { "Issued": "is issued", "Void": "is void" },
              "triggers": { "Cancel": { "phrase": "{subject} is voided" } },
              "links": { "the receipt for {subject}": { "machine": "order", "field": "orderId" } }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open" },
              "triggers": { "Open": { "phrase": "the shop opens" } }
            }
          }
        }
        """;

    private const string Background = """
        Feature: Bakery
          Background:
            Given Ada is a customer
            And Bo is a customer
            And Ada has an order
            And Ada has a receipt

          Scenario: A receipt is voided
            When Ada's receipt is voided
            Then Ada's receipt is void

          Scenario: An order starts baking
            Given Ada's order is placed
            When Ada's order starts baking
            Then Ada's order is baking

        """;

    private const string Expiring = """
          Scenario: An expired order voids its receipt
            Given Ada's order is placed
            When Ada lets her order expire
            Then Ada's order has expired
            And Ada's receipt is void
        """;

    [Test]
    public void One_users_machine_moving_another_of_theirs_is_a_linked_policy_through_the_glossarys_link()
    {
        var derived = Derive(Background + Expiring);

        derived.Problems.Should().BeEmpty();
        var policy = HostFile.Read(derived.Host!).Policies.Should().ContainSingle().Subject;
        policy.Shape.Should().Be("linked");
        (policy.When, policy.Enters, policy.Target, policy.Trigger, policy.By)
            .Should()
            .Be(("order", "Expired", "receipt", "Cancel", "orderId"));
        HostFile
            .Read(derived.Host!)
            .Machines["receipt"]
            .Triggers["Cancel"]
            .IsServer.Should()
            .BeTrue();
    }

    [Test]
    public void A_system_machine_moving_users_instances_is_a_broadcast_to_those_waiting()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: The shop opening starts the waiting orders
                    Given the shop is closed
                    And Ada's order is placed
                    When the shop opens
                    Then the shop is open
                    And Ada's order is baking
                """
        );

        derived.Problems.Should().BeEmpty();
        var policy = HostFile.Read(derived.Host!).Policies.Should().ContainSingle().Subject;
        (policy.Shape, policy.When, policy.Enters, policy.Target, policy.By, policy.Trigger)
            .Should()
            .Be(("broadcast", "shop", "Open", "order", "Placed", "Bake"));
    }

    [Test]
    public void A_policy_into_a_state_of_a_users_machine_that_runs_a_train_is_refused()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        var order = glossary["machines"]!["order"]!.AsObject();
        order["states"]!["Baked"] = "is baked";
        order["states"]!["Burnt"] = "is burnt";
        order["invokes"] = JsonNode.Parse(
            """
            {
              "Baking": {
                "train": "Shop.IBake",
                "outcomes": {
                  "the bread is done": { "outcome": "done" },
                  "the oven fails": { "outcome": "failed" },
                  "the baking is called off": { "outcome": "cancelled" }
                }
              }
            }
            """
        );
        static string Outcome(string said, string then) =>
            $"""

                  Scenario: {said}
                    Given Ada's order is placed
                    When Ada's order starts baking
                    Then Ada's order is baking
                    When {said}
                    Then Ada's order {then}

                """;

        var derived = Derive(
            Background
                + Outcome("the bread is done", "is baked")
                + Outcome("the oven fails", "is burnt")
                + Outcome("the baking is called off", "is burnt")
                + """
                  Scenario: The shop opening starts the waiting orders
                    Given the shop is closed
                    And Ada's order is placed
                    When the shop opens
                    Then the shop is open
                    And Ada's order is baking
                """,
            glossary.ToJsonString()
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Baking, which runs a train");
    }

    [Test]
    public void A_derived_directory_with_policies_is_served_whole()
    {
        var derived = Derive(Background + Expiring);
        var directory = Directory.CreateTempSubdirectory("trax-derived-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "host.json"), derived.Host!);
            foreach (var (id, ir) in derived.Irs)
                File.WriteAllText(Path.Combine(directory, $"{id}.ir.json"), ir);

            var loaded = IrDirectory.Load(directory, []);

            loaded.Machines.Select(m => m.Name).Should().BeEquivalentTo(derived.Irs.Keys);
            loaded.Host.Policies.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Another_owners_instance_moving_is_refused()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Ada's order voids Bo's receipt
                    Given Bo has a receipt
                    And Ada's order is placed
                    When Ada lets her order expire
                    Then Ada's order has expired
                    And Bo's receipt is void
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("A policy acts on the same owner's instances");
    }

    [Test]
    public void The_same_owner_without_a_link_is_refused_as_ambiguous()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        glossary["machines"]!["receipt"]!.AsObject().Remove("links");

        var derived = Derive(Background + Expiring, glossary.ToJsonString());

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("Add a link");
    }

    [Test]
    public void A_move_no_scenario_shows_a_trigger_for_is_a_problem_asking_for_one()
    {
        var derived = Derive(
            """
            Feature: Bakery
              Scenario: An expired order voids its receipt
                Given Ada is a customer
                And Ada has an order
                And Ada has a receipt
                And Ada's order is placed
                When Ada lets her order expire
                Then Ada's order has expired
                And Ada's receipt is void
            """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("no scenario shows a trigger doing that");
    }

    [Test]
    public void A_When_that_moved_nothing_sets_nothing_off()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Expiring a draft
                    When Ada lets her order expire
                    Then Ada's order is still a draft
                    And Ada's receipt is void
                """
        );

        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("the When before it moved nothing"));
    }

    private static Derived Derive(string feature, string glossary = Glossary) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("bakery.feature", feature)]
        );
}
