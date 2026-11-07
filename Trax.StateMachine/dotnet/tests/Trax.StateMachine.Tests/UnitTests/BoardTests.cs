using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The board is drawn from the IRs and the host file alone (ADR <c>Trax.Docs/adr/0058</c>): each sticky has its flow
/// role and the scenarios behind it, and the same files draw the same board.
/// </summary>
public class BoardTests
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
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Place": { "phrase": "places {subject}" }
              },
              "guards": {
                "hasLoaves": {
                  "holds": "has something in it",
                  "fails": "has nothing in it",
                  "rule": { "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }
                }
              }
            }
          },
          "views": {
            "orders": { "reads": [ { "machine": "order" } ], "fields": { "status": { "from": "order.state" } } }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Background:
            Given Ada is a customer
            And Ada has an order

          Scenario: Placing
            Given Ada's order has something in it
            When Ada places her order
            Then Ada's order is placed
            And Ada's orders show status "Placed"

          Scenario: Placing nothing
            Given Ada's order has nothing in it
            When Ada places her order
            Then Ada is told "Add something first."
        """;

    [Test]
    public void Each_sticky_has_its_role_and_the_scenarios_behind_it()
    {
        var board = Draw();

        Kinds(board)
            .Should()
            .Contain([
                (Board.Kind.Actor, "customer"),
                (Board.Kind.Command, "Place"),
                (Board.Kind.Event, "Draft → Placed"),
                (Board.Kind.Policy, "only if allowed: Place from Draft"),
                (Board.Kind.Aggregate, "order"),
                (Board.Kind.View, "orders"),
            ]);
        board
            .Stickies.Single(s => s.Kind == Board.Kind.Policy)
            .Scenarios.Select(s => s.Scenario)
            .Should()
            .Equal("Placing nothing");
        board.Links.Should().Contain(new Board.Link("aggregate:order", "view:orders"));
        board.Links.Should().Contain(new Board.Link("actor:customer", "command:order:Place"));
    }

    [Test]
    public void The_same_files_draw_the_same_board()
    {
        Kinds(Draw()).Should().Equal(Kinds(Draw()));
        Draw().Links.Should().Equal(Draw().Links);
    }

    private static List<(Board.Kind, string)> Kinds(Board.Drawn board) =>
        board.Stickies.Select(s => (s.Kind, s.Label)).ToList();

    private static Board.Drawn Draw()
    {
        var derived = FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            [("ordering.feature", Ordering)]
        );
        derived.Problems.Should().BeEmpty();
        return Board.Draw(
            derived.Irs.ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value)!.AsObject()),
            HostFile.Read(derived.Host!)
        );
    }
}
