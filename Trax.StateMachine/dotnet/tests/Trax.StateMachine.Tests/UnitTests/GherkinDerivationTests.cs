using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// Machines are derived from features and a glossary, and derivation never writes a machine its own features fail
/// (ADR <c>Trax.Docs/adr/0058</c>). Each test is one of the ways the proof of concept wrote a wrong machine or skipped
/// what it could not read; after the fix each is a problem naming the step, or the machine its scenarios describe.
/// </summary>
public class GherkinDerivationTests
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
              "context": [
                { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] },
                { "name": "note", "type": "string", "nullable": false, "constraints": [] }
              ],
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Note": {
                  "phrase": "notes {text} and {more} on {subject}",
                  "input": [
                    { "name": "text", "type": "string", "nullable": false, "constraints": [] },
                    { "name": "more", "type": "string", "nullable": false, "constraints": [] }
                  ]
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
          }
        }
        """;

    private const string Background = """
        Feature: Ordering bread
          Background:
            Given Ada is a customer
            And Ada has an order

        """;

    private const string Placing = """
          Scenario: Ordering loaves
            When Ada orders "3" loaves for her order
            Then Ada's order is a draft

          Scenario: Placing an order with something in it
            Given Ada's order has something in it
            When Ada places her order
            Then Ada's order is placed

          Scenario: Placing an empty order
            Given Ada's order has nothing in it
            When Ada places her order
            Then Ada is told "Add something first."
            And Ada's order is still a draft
        """;

    [Test]
    public void The_features_derive_a_guarded_edge_with_its_refusal_and_a_machine_that_can_be_served()
    {
        var derived = Derive(Background + Placing);

        derived.Problems.Should().BeEmpty();
        var ir = JsonNode.Parse(derived.Irs["order"])!.AsObject();
        var place = ir["transitions"]!
            .AsArray()
            .Single(t => t!["trigger"]!.GetValue<string>() == "Place")!;
        place["from"]!.GetValue<string>().Should().Be("Draft");
        place["to"]!.GetValue<string>().Should().Be("Placed");
        place["guard"]!["field"]!.GetValue<string>().Should().Be("loaves");
        place["guardMessage"]!.GetValue<string>().Should().Be("Add something first.");
        var order = ir["transitions"]!
            .AsArray()
            .Single(t => t!["trigger"]!.GetValue<string>() == "Order")!;
        order["to"]!.GetValue<string>().Should().Be("Draft");
        order["reduce"].Should().NotBeNull("a trigger's glossary reduction is its edges'");

        var host = HostFile.Read(derived.Host!);
        var served = IrMachines.Create(new IrMachines.Source(ir, host.Machines["order"]), []);
        served
            .ExportIr()
            .Should()
            .Be(derived.Irs["order"], "a derived machine is one the host serves as written");
    }

    [Test]
    public void A_typo_in_a_Given_is_a_problem_naming_the_step_not_an_edge_from_the_initial_state()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Placing a full order
                    Given Ada's order has somethin in it
                    When Ada places her order
                    Then Ada's order is placed
                """
        );

        derived.Irs.Should().BeEmpty();
        derived
            .Problems.Should()
            .ContainSingle(p => p.Line == 6)
            .Which.Message.Should()
            .Contain("has somethin in it");
    }

    [Test]
    public void A_refusal_message_with_no_scenario_where_the_trigger_works_is_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Placing an empty order
                    Given Ada's order has nothing in it
                    When Ada places her order
                    Then Ada is told "Add something first."
                    And Ada's order is still a draft
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("no guard to give it");
    }

    [Test]
    public void A_step_that_leaves_a_required_input_unset_is_refused_at_derivation()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        glossary["machines"]!["order"]!["triggers"]!["Note"]!["phrase"] =
            "notes {text} on {subject}";

        var derived = FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary.ToJsonString()),
            [
                (
                    "order.feature",
                    Background
                        + """
                          Scenario: Noting
                            When Ada notes "rye" on her order
                            Then Ada's order is a draft
                        """
                ),
            ]
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("leaves 'more' unset");
    }

    [Test]
    public void A_value_is_read_as_its_fields_type_and_one_that_is_not_is_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Ordering a word
                    When Ada orders "three" loaves for her order
                    Then Ada's order is a draft
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("\"three\" is not an integer");
    }

    [Test]
    public void Each_outline_row_is_its_own_scenario_identified_by_its_line()
    {
        var derived = Derive(
            Background
                + """
                  Scenario Outline: Ordering a number of loaves
                    When Ada orders "<n>" loaves for her order
                    Then Ada's order is a draft
                    Examples:
                      | n     |
                      | 2     |
                      | seven |
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Line.Should()
            .Be(6, "the step is reported where it is written");
        derived
            .Problems.Single()
            .Message.Should()
            .Contain("order.feature:11", "the failing row is named by its own line");
    }

    [TestCase("    Given Ada's order is a draft\n      | a |\n      | b |\n", "data table")]
    [TestCase(
        "    Given Ada's order is a draft\n      \"\"\"\n      text\n      \"\"\"\n",
        "DocString"
    )]
    [TestCase("    * Ada's order is a draft\n", "has no Given, When or Then")]
    public void Gherkin_derivation_cannot_read_is_refused_not_skipped(string step, string problem)
    {
        var derived = Derive(Background + "  Scenario: Odd\n" + step);

        derived.Problems.Should().Contain(p => p.Message.Contains(problem));
    }

    [Test]
    public void A_step_that_speaks_as_I_is_refused_asking_for_a_name()
    {
        var derived = Derive(
            """
            Feature: Ordering bread
              Scenario: In the first person
                Given I am a customer
            """
        );

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("Name the person");
    }

    [Test]
    public void Two_scenarios_that_disagree_are_refused_naming_both()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Placing works
                    Given Ada's order has something in it
                    When Ada places her order
                    Then Ada's order is placed

                  Scenario: Placing is refused anyway
                    Given Ada's order has something in it
                    When Ada places her order
                    Then Ada's order is still a draft
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("order.feature:5")
            .And.Contain("no guard");
    }

    [Test]
    public void An_unresolved_Then_is_a_step_about_the_UI_not_a_problem()
    {
        var derived = Derive(
            Background
                + Placing
                + """

                  Scenario: The basket shows the count
                    When Ada orders "2" loaves for her order
                    Then Ada's order is a draft
                    And the basket shows two loaves
                """
        );

        derived.Problems.Should().BeEmpty();
        derived
            .UiSteps.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Be("the basket shows two loaves");
    }

    [Test]
    public void A_person_the_scenario_never_introduced_is_a_problem()
    {
        var derived = Derive(
            """
            Feature: Ordering bread
              Scenario: Nobody said who Bo is
                Given Bo has an order
            """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("never says who Bo is");
    }

    [Test]
    public void A_pronoun_with_no_person_in_its_step_is_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Whose order
                    Then her order is a draft
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("'her' names the step's own person");
    }

    [Test]
    public void An_orphan_told_is_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Told about nothing
                    Then Ada is told "Add something first."
                """
        );

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("answers no act");
    }

    private static Derived Derive(string feature) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            [("order.feature", feature)]
        );
}
