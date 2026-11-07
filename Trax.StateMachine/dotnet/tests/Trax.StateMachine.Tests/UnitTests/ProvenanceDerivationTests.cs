using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The host file says which scenarios produced each part of each machine (ADR <c>Trax.Docs/adr/0058</c>): by file, line
/// and name, never by step text, sorted so that the order the feature files are given in changes nothing.
/// </summary>
public class ProvenanceDerivationTests
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
              "initial": "Draft",
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Baking": "is baking" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                }
              }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open" },
              "triggers": { "Open": { "phrase": "the shop opens" } }
            }
          },
          "views": {
            "orders": {
              "reads": [ { "machine": "order" } ],
              "fields": {
                "status": { "from": "order.state", "labels": { "Draft": "Not sent", "Baking": "In the oven" } },
                "loaves": { "from": "order.loaves" }
              }
            },
            "shop sign": {
              "reads": [ { "machine": "shop", "key": "shop/main" } ],
              "fields": { "open": { "from": "shop.state", "labels": { "Open": "We're open", "Closed": "Closed" } } }
            }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Background:
            Given Ada is a customer
            And Ada has an order

          Scenario: Ada orders loaves
            When Ada orders "3" loaves for her order
            Then Ada's order is a draft
            And Ada's orders show loaves "3"
        """;

    private const string More = """
        Feature: More ordering
          Scenario: Bo orders too
            Given Bo is a customer
            And Bo has an order
            When Bo orders "2" loaves for his order
            Then Bo's order is a draft
        """;

    [Test]
    public void Each_part_names_the_scenarios_that_produced_it()
    {
        var provenance = Provenance([("a.feature", Ordering), ("b.feature", More)]);

        provenance["order"]!["start"]!
            .AsArray()
            .Select(s => s!["file"] + ":" + s["line"] + " " + s["scenario"])
            .Should()
            .Equal("a.feature:6 Ada orders loaves", "b.feature:2 Bo orders too");
        provenance["order"]!
            .AsObject()
            .Select(kv => kv.Key)
            .Should()
            .Equal("command Order", "edge Draft Order Draft", "start", "view orders");
    }

    [Test]
    public void It_holds_no_step_text()
    {
        Provenance([("a.feature", Ordering)]).ToJsonString().Should().NotContain("loaves for");
    }

    [Test]
    public void The_order_of_the_files_changes_nothing()
    {
        Provenance([("b.feature", More), ("a.feature", Ordering)])
            .ToJsonString()
            .Should()
            .Be(Provenance([("a.feature", Ordering), ("b.feature", More)]).ToJsonString());
    }

    private static JsonObject Provenance(IReadOnlyList<(string, string)> features)
    {
        var derived = FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            features
        );
        derived.Problems.Should().BeEmpty();
        return JsonNode.Parse(derived.Host!)!["provenance"]!.AsObject();
    }
}
