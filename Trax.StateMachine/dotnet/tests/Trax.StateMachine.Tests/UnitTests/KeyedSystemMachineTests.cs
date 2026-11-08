using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A keyed system machine (<c>system: { "keyedBy": [...] }</c>): one instance per key, started and moved by host code,
/// named in a step by its name and key (<c>ticket "T-12"</c>). No person fires it, no view reads it and no policy moves
/// it or is set off by it.
/// </summary>
public class KeyedSystemMachineTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "fixer": { "noun": "a fixer", "roles": ["fixer"] } },
          "machines": {
            "ticket": {
              "system": { "keyedBy": ["id"] },
              "name": "ticket",
              "initial": "New",
              "states": { "New": "is new", "Triaged": "is in triage", "Closed": "is closed" },
              "triggers": {
                "Triage": { "phrase": "{subject} gets triaged" },
                "Close": { "phrase": "{subject} gets closed" }
              }
            },
            "shift": {
              "ownedBy": "fixer",
              "noun": "shift",
              "start": "has a shift",
              "initial": "Off",
              "states": { "Off": "is off", "On": "is on" },
              "triggers": { "ClockIn": { "phrase": "clocks in for {subject}" } }
            }
          }
        }
        """;

    private const string Tickets = """
        Feature: Tickets
          Scenario: A ticket in triage is closed
            Given ticket "T-12" is in triage
            When ticket "T-12" gets closed
            Then ticket "T-12" is closed

          Scenario: Each ticket moves on its own
            Given ticket "T-12" is new
            And ticket "T-13" is new
            When ticket "T-12" gets triaged
            Then ticket "T-12" is in triage
            And ticket "T-13" is new

          Scenario: A fixer clocks in
            Given Fay is a fixer
            And Fay has a shift
            When Fay clocks in for her shift
            Then Fay's shift is on

        """;

    [Test]
    public void A_keyed_system_machine_is_derived_and_fired_by_the_server_only()
    {
        var derived = Derive(Tickets);

        derived.Problems.Should().BeEmpty();
        var host = JsonNode.Parse(derived.Host!)!["machines"]!["ticket"]!;
        host["owner"]!.ToJsonString().Should().Be("""{"system":{"keyedBy":["id"]}}""");
        host["triggers"]!
            .ToJsonString()
            .Should()
            .Be("""{"Close":{"server":true},"Triage":{"server":true}}""");
        derived.Access.Should().Contain("ticket Close: the server only\n");
        HostFile.Read(derived.Host!).Machines["ticket"].KeyedBy.Should().Equal("id");
    }

    [Test]
    public void It_is_served_as_a_system_machine_and_the_host_starts_none_of_it()
    {
        var derived = Derive(Tickets);
        var file = HostFile.Read(derived.Host!);

        var machine = IrMachines.Create(
            new IrMachines.Source(
                JsonNode.Parse(derived.Irs["ticket"])!.AsObject(),
                file.Machines["ticket"]
            ),
            []
        );

        ((IMachineInternals)machine).SystemOwned.Should().BeTrue();
        file.Machines["ticket"].SystemKey.Should().BeNull("there is no fixed key to start at");
    }

    [Test]
    public void A_person_firing_a_keyed_system_machine_is_refused()
    {
        var derived = Derive(
            """
            Feature: Tickets
              Scenario: Fay closes a ticket
                Given Fay is a fixer
                And ticket "T-12" is in triage
                When Fay ticket "T-12" gets closed
                Then ticket "T-12" is closed
            """
        );

        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("only the server moves a system machine"));
    }

    [Test]
    public void A_step_that_names_no_key_matches_nothing()
    {
        var derived = Derive(
            """
            Feature: Tickets
              Scenario: A ticket with no key
                Given ticket is new
                When ticket gets triaged
                Then ticket is in triage
            """
        );

        derived.Problems.Should().Contain(p => p.Message.Contains("No phrase in the glossary"));
    }

    [Test]
    public void A_policy_set_off_by_a_keyed_system_machine_is_refused()
    {
        var derived = Derive(
            """
            Feature: Tickets
              Scenario: Triage clocks a fixer in
                Given Fay is a fixer
                And Fay has a shift
                And ticket "T-12" is new
                When ticket "T-12" gets triaged
                Then ticket "T-12" is in triage
                And Fay's shift is on

              Scenario: A fixer clocks in
                Given Fay is a fixer
                And Fay has a shift
                When Fay clocks in for her shift
                Then Fay's shift is on
            """
        );

        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("ticket is a keyed system machine"));
    }

    [Test]
    public void A_view_of_a_keyed_system_machine_is_refused()
    {
        var glossary = Glossary.Replace(
            "\"machines\": {",
            """
            "views": { "tickets": { "reads": [ { "machine": "ticket" } ], "fields": { "status": { "from": "ticket.state" } } } },
            "machines": {
            """
        );

        var read = () => Trax.StateMachine.Features.Glossary.Read(glossary);

        read.Should().Throw<FormatException>().WithMessage("*keyed system machine*");
    }

    private static Derived Derive(string feature, string glossary = Glossary) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("tickets.feature", feature)]
        );
}
