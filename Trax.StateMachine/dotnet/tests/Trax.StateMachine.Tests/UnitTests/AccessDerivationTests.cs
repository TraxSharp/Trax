using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// Access is derived from who the features show doing what, and never widened (ADR <c>Trax.Docs/adr/0058</c>). Only a
/// machine's owner acts on an instance (MS-18 decision 1), an actor's own act grants it the trigger (decision 10), and
/// every grant is in the access table. The proof of concept served a trigger a scenario denied to any signed-in caller,
/// derived machines no one could drive, and let one actor act on another's instance in memory only; each is a problem
/// here.
/// </summary>
public class AccessDerivationTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "customer": { "noun": "a customer", "roles": ["customer"] },
            "manager": { "noun": "a manager", "roles": ["manager", "customer"] },
            "member": { "noun": "a member", "anyAuthenticated": true }
          },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed", "Baking": "is baking" },
              "triggers": {
                "Place": { "phrase": "places {subject}" },
                "Reopen": { "phrase": "reopens {subject}" },
                "Bake": { "phrase": "{subject} starts baking" },
                "Rush": { "phrase": "rushes {subject}" }
              }
            },
            "note": {
              "ownedBy": "member",
              "noun": "note",
              "start": "has a note",
              "initial": "Open",
              "states": { "Open": "is open", "Shut": "is shut" },
              "triggers": { "Shut": { "phrase": "shuts {subject}" }, "Open": { "phrase": "opens {subject}" } }
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

    private const string People = """
        Feature: Ordering
          Background:
            Given Ada is a customer
            And Bo is a customer
            And Ada has an order

          Scenario: Placing
            When Ada places her order
            Then Ada's order is placed

        """;

    [Test]
    public void An_owners_act_grants_their_actor_the_trigger_and_the_grant_is_in_the_table()
    {
        var derived = Derive(People);

        derived.Problems.Should().BeEmpty();
        var order = HostFile.Read(derived.Host!).Machines["order"];
        order.Start!.RoleSets.Should().ContainSingle().Which.Should().Equal("customer");
        order.Read!.RoleSets.Should().ContainSingle().Which.Should().Equal("customer");
        order
            .Triggers["Place"]
            .Client!.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .Equal("customer");
        derived.Access.Should().Contain("order Place: customer");
    }

    [Test]
    public void An_actor_a_rule_admits_that_no_scenario_shows_is_marked_in_the_table()
    {
        var derived = Derive(People);

        derived
            .Access.Should()
            .Contain(
                "order Place: customer, manager (no scenario)",
                "a manager holds the customer role"
            );
    }

    [Test]
    public void A_trigger_no_scenario_fires_is_not_served()
    {
        var derived = Derive(People);

        var ir = JsonNode.Parse(derived.Irs["order"])!;
        ir["triggers"]!.AsArray().Select(t => t!.GetValue<string>()).Should().Equal("Place");
        HostFile.Read(derived.Host!).Machines["order"].Triggers.Should().NotContainKey("Rush");
    }

    [Test]
    public void The_derived_host_file_passes_the_hosts_own_startup_checks()
    {
        var derived = Derive(People);
        var ir = JsonNode.Parse(derived.Irs["order"])!.AsObject();

        var machine = IrMachines.Create(
            new IrMachines.Source(ir, HostFile.Read(derived.Host!).Machines["order"]),
            []
        );

        MachineRulesStartupValidator
            .Problems([machine], new StateMachineOptions(), null)
            .Result.Should()
            .BeEmpty();
    }

    [Test]
    public void One_person_acting_on_anothers_instance_is_refused_at_derivation()
    {
        var derived = Derive(
            People
                + """
                  Scenario: Bo places Ada's order
                    When Bo places Ada's order
                    Then Ada's order is placed
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Bo acts on Ada's order");
    }

    [Test]
    public void A_person_firing_a_system_machine_is_refused_at_derivation()
    {
        var derived = Derive(
            People
                + """
                  Scenario: Ada opens the shop
                    Given the shop is closed
                    When Ada the shop opens
                    Then the shop is open
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("only the server moves a system machine");
    }

    [Test]
    public void An_actor_allowed_in_one_scenario_and_denied_in_another_is_a_problem_not_a_widened_rule()
    {
        var derived = Derive(
            People
                + """
                  Scenario: Bo may not place
                    Given Bo has an order
                    When Bo places his order
                    Then Bo is not allowed
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Bo may not Place here, and Ada");
    }

    [Test]
    public void A_trigger_both_a_person_and_the_server_fire_is_refused()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        glossary["machines"]!["order"]!["triggers"]!["Bake"]!["phrase"] = "bakes {subject}";
        var derived = Derive(
            People
                + """
                  Scenario: Ada bakes
                    Given Ada's order is placed
                    When Ada bakes her order
                    Then Ada's order is baking

                  Scenario: Bo's order bakes on its own
                    Given Bo's order is placed
                    When bakes Bo's order
                    Then Bo's order is baking
                """,
            glossary.ToJsonString()
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("make them two triggers");
    }

    [Test]
    public void A_role_gated_trigger_back_to_the_start_state_is_refused_worded_like_the_host()
    {
        var derived = Derive(
            People
                + """
                  Scenario: Reopening
                    Given Ada's order is placed
                    When Ada reopens her order
                    Then Ada's order is a draft
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("the rule would gate nothing");
    }

    [Test]
    public void A_trigger_only_steps_with_no_person_fire_is_the_servers()
    {
        var derived = Derive(
            People
                + """
                  Scenario: Baking starts
                    Given Ada's order is placed
                    When Ada's order starts baking
                    Then Ada's order is baking
                """
        );

        HostFile.Read(derived.Host!).Machines["order"].Triggers["Bake"].IsServer.Should().BeTrue();
        derived.Access.Should().Contain("order Bake: the server only");
    }

    [Test]
    public void Any_signed_in_owner_may_return_their_instance_to_the_start_state()
    {
        var derived = Derive(
            """
            Feature: Notes
              Scenario: Shutting and opening
                Given Cy is a member
                And Cy has a note
                When Cy shuts his note
                Then Cy's note is shut
                When Cy opens his note
                Then Cy's note is open
            """
        );

        derived
            .Problems.Should()
            .BeEmpty("a rule that gates nothing is only a problem when it names a role");
        HostFile
            .Read(derived.Host!)
            .Machines["note"]
            .Triggers["Open"]
            .Client!.IsAnyAuthenticated.Should()
            .BeTrue();
    }

    private static Derived Derive(string feature, string glossary = Glossary) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("order.feature", feature)]
        );
}
