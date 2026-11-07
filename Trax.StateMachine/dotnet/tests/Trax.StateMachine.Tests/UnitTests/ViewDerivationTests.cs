using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A view's readers come from the features, as access does (ADR <c>Trax.Docs/adr/0058</c>): the actors whose people
/// read it with <c>Ada's orders show status "..."</c>. A view no scenario reads is not served, a view shows a person only
/// their own instances and the system's, and a value the scenario's instances do not show is a problem.
/// </summary>
public class ViewDerivationTests
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

          Scenario: The orders show what was ordered
            When Ada orders "3" loaves for her order
            Then Ada's order is a draft
            And Ada's orders show status "Not sent"
            And Ada's orders show loaves "3"
        """;

    [Test]
    public void A_views_readers_are_the_actors_whose_people_read_it()
    {
        var derived = Derive(Ordering);

        derived.Problems.Should().BeEmpty();
        var view = HostFile.Read(derived.Host!).Views["orders"];
        view.Readers.RoleSets.Should().ContainSingle().Which.Should().Equal("customer");
        view.Fields.Select(f => f.Name).Should().BeEquivalentTo(["status", "loaves"]);
        derived.Access.Should().Contain("view orders: customer");
    }

    [Test]
    public void A_view_no_scenario_reads_is_not_served()
    {
        var derived = Derive(Ordering);

        HostFile.Read(derived.Host!).Views.Should().NotContainKey("shop sign");
    }

    [Test]
    public void A_view_shows_a_person_their_own_instances_only()
    {
        var derived = Derive(
            Ordering
                + """

                  Scenario: Bo's orders are his own
                    Given Bo is a customer
                    And Bo has an order
                    When Ada orders "3" loaves for her order
                    Then Ada's order is a draft
                    And Bo's orders show loaves "3"
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Bo's orders show loaves \"0\"");
    }

    [Test]
    public void A_system_instance_is_shown_to_everyone_who_reads_the_view()
    {
        var derived = Derive(
            """
            Feature: The shop
              Scenario: The sign says the shop is open
                Given Kit is a baker
                And the shop is closed
                When the shop opens
                Then the shop is open
                And Kit's shop sign shows open "We're open"
            """
        );

        derived.Problems.Should().BeEmpty();
        HostFile
            .Read(derived.Host!)
            .Views["shop sign"]
            .Readers.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .Equal("baker");
    }

    [Test]
    public void A_value_the_instances_do_not_show_is_a_problem()
    {
        var derived = Derive(
            Background()
                + """
                  Scenario: Wrong label
                    Then Ada's orders show status "In the oven"
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("show status \"Not sent\"");
    }

    private static string Background() =>
        """
            Feature: Ordering
              Background:
                Given Ada is a customer
                And Ada has an order

            """;

    private static Derived Derive(string feature) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            [("views.feature", feature)]
        );
}
