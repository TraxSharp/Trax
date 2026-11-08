using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// Time in features is derived as timers (ADR <c>Trax.Docs/adr/0058</c>): a scenario keeps a clock, an instance counts
/// from when it entered its state, a Then after time passes is a timer firing or a stay, and every stay is shorter than
/// the timer. Waits add up rather than each counting on its own, "still" is read rather than skipped, and a second
/// machine that reacts to the first through a policy gets no timer of its own.
/// </summary>
public class TimerDerivationTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "member": { "noun": "a member", "anyAuthenticated": true },
            "keeper": { "noun": "a keeper", "roles": ["keeper"] }
          },
          "machines": {
            "door": {
              "ownedBy": "member",
              "noun": "door",
              "start": "has a door",
              "initial": "Shut",
              "states": { "Shut": "is shut", "Open": "is open", "Closing": "is closing" },
              "triggers": { "Open": { "phrase": "opens {subject}" } }
            },
            "lamp": {
              "ownedBy": "member",
              "noun": "lamp",
              "start": "has a lamp",
              "initial": "Off",
              "states": { "Off": "is off", "On": "is on" },
              "triggers": { "Light": { "phrase": "lights {subject}" } }
            },
            "gate": {
              "ownedBy": "keeper",
              "noun": "gate",
              "start": "has a gate",
              "initial": "Down",
              "states": { "Down": "is down", "Up": "is up", "Lowering": "is lowering" },
              "triggers": { "Raise": { "phrase": "raises {subject}" } }
            }
          }
        }
        """;

    private const string Background = """
        Feature: Doors
          Background:
            Given Cy is a member
            And Cy has a door
            And Cy has a lamp

          Scenario: Opening
            When Cy opens his door
            Then Cy's door is open

        """;

    [Test]
    public void Waits_add_up_on_the_scenarios_clock()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: The door closes after a while
                    Given Cy's door is open
                    When 2 seconds pass
                    And 2 seconds pass
                    Then Cy's door is closing
                """
        );

        derived.Problems.Should().BeEmpty();
        Timer(derived, "door", "Open")["after"]!.GetValue<string>().Should().Be("PT4S");
    }

    [Test]
    public void Still_after_time_passes_is_a_stay_the_timer_must_be_longer_than()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Not yet
                    Given Cy's door is open
                    When 3 seconds pass
                    Then Cy's door is still open

                  Scenario: Then it closes
                    Given Cy's door is open
                    When 2 seconds pass
                    Then Cy's door is closing
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("still Open after PT3S here, and leaves it after PT2S");
    }

    [Test]
    public void A_stay_shorter_than_the_timer_agrees_with_it()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Not yet, then closing
                    Given Cy's door is open
                    When 1 second passes
                    Then Cy's door is still open
                    When 1 second passes
                    Then Cy's door is closing
                """
        );

        derived.Problems.Should().BeEmpty();
        Timer(derived, "door", "Open")["after"]!.GetValue<string>().Should().Be("PT2S");
    }

    [Test]
    public void A_second_machine_changing_after_time_passes_is_a_policy_not_a_timer_of_its_own()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: The lamp goes out with the door
                    Given Cy's door is open
                    And Cy's lamp is on
                    When 5 seconds pass
                    Then Cy's door is closing
                    And Cy's lamp is off
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain(
                "no scenario shows a trigger doing that",
                "the lamp going off is a policy reacting to the door, so a scenario must show the trigger that turns it off"
            );
    }

    [Test]
    public void A_derived_timed_machine_is_served_with_the_timer_rules_its_access_makes()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: The door closes
                    Given Cy's door is open
                    When 2 seconds pass
                    Then Cy's door is closing
                """
        );

        var ir = JsonNode.Parse(derived.Irs["door"])!.AsObject();
        Timer(derived, "door", "Open")["rules"]!
            .AsArray()
            .Select(r => r!.GetValue<string>())
            .Should()
            .Equal(
                ["AnyAuthenticated"],
                "Open is entered only by the Open trigger, which any signed-in member fires"
            );

        var served = IrMachines.Create(
            new IrMachines.Source(ir, HostFile.Read(derived.Host!).Machines["door"]),
            []
        );

        served.ExportIr().Should().Be(derived.Irs["door"]);
    }

    [Test]
    public void A_role_gated_entry_is_in_the_timers_rules()
    {
        var derived = Derive(
            """
            Feature: Gates
              Scenario: The gate comes down on its own
                Given Kit is a keeper
                And Kit has a gate
                When Kit raises his gate
                Then Kit's gate is up
                When 10 seconds pass
                Then Kit's gate is lowering
            """
        );

        derived.Problems.Should().BeEmpty();
        Timer(derived, "gate", "Up")["rules"]!
            .AsArray()
            .Select(r => r!.GetValue<string>())
            .Should()
            .Equal(["Requires(keeper)"]);
        var ir = JsonNode.Parse(derived.Irs["gate"])!.AsObject();
        var served = IrMachines.Create(
            new IrMachines.Source(ir, HostFile.Read(derived.Host!).Machines["gate"]),
            []
        );
        served.ExportIr().Should().Be(derived.Irs["gate"]);
    }

    [Test]
    public void Leaving_a_state_with_no_time_passing_there_is_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Instantly
                    Given Cy's door is open
                    When 0 seconds pass
                    Then Cy's door is closing
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("with no time passing there");
    }

    [Test]
    public void A_wait_longer_than_a_clock_holds_is_refused_as_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Forever
                    Given Cy's door is open
                    When 999999999 days pass
                    Then Cy's door is closing
                """
        );

        derived
            .Problems.Should()
            .Contain(p => p.Message.StartsWith("'999999999 days' is longer than a clock holds"));
    }

    [Test]
    public void Waits_that_add_up_past_what_a_clock_holds_are_refused_as_a_problem()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Forever, twice
                    Given Cy's door is open
                    When 9999999 days pass
                    And 9999999 days pass
                    Then Cy's door is closing
                """
        );

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("adds up");
    }

    private static JsonNode Timer(Derived derived, string machine, string state) =>
        JsonNode.Parse(derived.Irs[machine])!["timers"]!
            .AsArray()
            .Single(t => t!["state"]!.GetValue<string>() == state)!;

    private static Derived Derive(string feature) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            [("doors.feature", feature)]
        );
}
