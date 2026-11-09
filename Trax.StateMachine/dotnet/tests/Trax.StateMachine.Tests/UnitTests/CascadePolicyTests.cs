using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// When one act moves a chain of instances, each move's cause is read from the chain (ADR <c>Trax.Docs/adr/0058</c>): a
/// baker clocking in opens the system's shop, and another person's waiting order is moved by the shop opening, a
/// broadcast, not by the baker's shift, which no policy may reach across owners from.
/// </summary>
public class CascadePolicyTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "customer": { "noun": "a customer", "roles": ["customer"] },
            "baker": { "noun": "a baker", "roles": ["baker"] }
          },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Placed",
              "states": { "Placed": "is placed", "Baking": "is baking" },
              "triggers": { "Bake": { "phrase": "{subject} starts baking" } }
            },
            "shift": {
              "ownedBy": "baker",
              "noun": "shift",
              "start": "has a shift",
              "initial": "Off",
              "states": { "Off": "is not started", "On": "is on" },
              "triggers": { "ClockIn": { "phrase": "clocks in for {subject}" } }
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

    private const string Shop = """
        Feature: The shop
          Scenario: Clocking in bakes the waiting orders
            Given Ada is a customer
            And Ada has an order
            And Kit is a baker
            And Kit has a shift
            And the shop is closed
            When Kit clocks in for his shift
            Then Kit's shift is on
            And the shop is open
            And Ada's order is baking

          Scenario: The shop opens
            Given the shop is closed
            When the shop opens
            Then the shop is open

          Scenario: An order bakes
            Given Ada is a customer
            And Ada has an order
            When Ada's order starts baking
            Then Ada's order is baking
        """;

    [Test]
    public void Another_persons_instance_in_the_chain_is_moved_by_the_system_instance_before_it()
    {
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("shop.feature", Shop)]
        );

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        var policies = JsonNode.Parse(derived.Host!)!["policies"]!.AsArray();
        policies
            .Select(p =>
                $"{p!["shape"]} {p["when"]!["machine"]}.{p["when"]!["enters"]} {p["then"]!["machine"]}.{p["then"]!["trigger"]}"
            )
            .Should()
            .BeEquivalentTo(["toSystem shift.On shop.Open", "broadcast shop.Open order.Bake"]);
    }
}
