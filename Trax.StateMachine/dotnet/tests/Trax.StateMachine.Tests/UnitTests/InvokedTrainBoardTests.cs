using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A machine whose state invokes a train (ADR <c>Trax.Docs/adr/0058</c>): its board draws each outcome's move, and the
/// states the outcomes reach are not reported as missing. The IR keys its outcomes by state and kind.
/// </summary>
public class InvokedTrainBoardTests
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
              "context": [ { "name": "approved", "type": "boolean", "nullable": true, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Paying": "is being paid for", "Placed": "is placed", "Declined": "was declined", "Unpaid": "is unpaid" },
              "triggers": { "Place": { "phrase": "places {subject}" } },
              "invokes": {
                "Paying": {
                  "train": "Shop.ITakePayment",
                  "output": [ { "name": "approved", "type": "boolean", "nullable": false, "constraints": [] } ],
                  "outcomes": {
                    "the payment goes through": {
                      "outcome": "done",
                      "when": { "rule": "boolEquals", "source": "input", "field": "approved", "value": true }
                    },
                    "the payment is declined": {
                      "outcome": "done",
                      "when": { "rule": "boolEquals", "source": "input", "field": "approved", "value": false }
                    },
                    "the payment cannot be taken": { "outcome": "failed" },
                    "the payment is called off": { "outcome": "cancelled" }
                  },
                  "examples": {
                    "the payment goes through": { "approved": true },
                    "the payment is declined": { "approved": false }
                  }
                }
              }
            }
          }
        }
        """;

    private static string Paying(string outcome, string then) =>
        $"""

              Scenario: {outcome}
                When Ada places her order
                Then Ada's order is being paid for
                When {outcome}
                Then Ada's order {then}
            """;

    private static readonly string Ordering =
        "Feature: Paying\n  Background:\n    Given Ada is a customer\n    And Ada has an order\n"
        + Paying("the payment goes through", "is placed")
        + Paying("the payment is declined", "was declined")
        + Paying("the payment cannot be taken", "is unpaid")
        + Paying("the payment is called off", "is unpaid");

    [Test]
    public void The_board_draws_each_outcomes_move()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(
            glossary,
            [new FeatureFile("paying.feature", Ordering)]
        );
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));

        var board = FeatureMachines.Board(derived.Irs, derived.Host!);

        board
            .Stickies.Where(s => s.Kind == "event")
            .Select(s => s.Label)
            .Should()
            .Contain(["Paying done → Placed", "Paying done → Declined", "Paying failed → Unpaid"]);
    }

    [Test]
    public void The_states_outcomes_reach_are_not_missing()
    {
        var gaps = FeatureMachines.Missing(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("paying.feature", Ordering)]
        );

        gaps.Should().NotContain(g => g.Kind == "state");
    }

    [Test]
    public void An_outcome_back_to_the_start_is_refused_as_the_host_refuses_it()
    {
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [
                new FeatureFile(
                    "paying.feature",
                    Ordering.Replace("Then Ada's order is unpaid", "Then Ada's order is a draft")
                ),
            ]
        );

        derived.Succeeded.Should().BeFalse();
        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("starts at Draft, and an outcome"));
    }

    [Test]
    public void A_timer_into_an_outcomes_state_is_refused_as_the_host_refuses_it()
    {
        var derived = Derive(
            Ordering
                + """

                  Scenario: A draft left alone
                    When 5 minutes pass
                    Then Ada's order is unpaid
                """
        );

        derived.Succeeded.Should().BeFalse();
        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Time takes order from Draft to Unpaid, where an outcome");
    }

    private static DerivedMachines Derive(string feature) =>
        FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("paying.feature", feature)]
        );
}
