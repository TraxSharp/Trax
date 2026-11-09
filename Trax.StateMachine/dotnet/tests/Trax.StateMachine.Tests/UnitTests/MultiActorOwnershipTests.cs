using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A machine several actors own (<c>ownedBy: [actors]</c>): each trigger is granted the actors the scenarios show
/// firing it, each on their own instances only, and a rule that would admit an actor a scenario denies is a problem,
/// never a widening (ADR <c>Trax.Docs/adr/0058</c>).
/// </summary>
public class MultiActorOwnershipTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "triage": { "noun": "a triage bot", "roles": ["triage"] },
            "fixer": { "noun": "a fixer", "roles": ["fixer"] },
            "visitor": { "noun": "a visitor", "roles": ["visitor"] }
          },
          "machines": {
            "assignment": {
              "ownedBy": ["triage", "fixer"],
              "noun": "assignment",
              "start": "has an assignment",
              "initial": "New",
              "states": { "New": "is new", "Triaged": "is triaged", "Fixed": "is fixed" },
              "triggers": {
                "Triage": { "phrase": "triages {subject}" },
                "SubmitFix": { "phrase": "submits a fix for {subject}" }
              }
            }
          }
        }
        """;

    private const string Lanes = """
        Feature: Lanes
          Scenario: A triage bot triages its assignment
            Given Tess is a triage bot
            And Tess has an assignment
            When Tess triages her assignment
            Then Tess's assignment is triaged

          Scenario: A fixer submits a fix
            Given Fay is a fixer
            And Fay has an assignment
            And Fay's assignment is triaged
            When Fay submits a fix for her assignment
            Then Fay's assignment is fixed

          Scenario: A fixer may not triage
            Given Fay is a fixer
            And Fay has an assignment
            When Fay triages her assignment
            Then Fay is not allowed

        """;

    [Test]
    public void Each_trigger_is_granted_the_actors_shown_firing_it()
    {
        var derived = Derive(Lanes);

        // Derivation replays every scenario on what it derived, the fixer's denial among them.
        derived.Problems.Should().BeEmpty();
        derived
            .Access.Should()
            .Contain("assignment Triage: triage\n")
            .And.Contain("assignment SubmitFix: fixer\n")
            .And.Contain("assignment start: fixer, triage\n")
            .And.Contain("assignment read: fixer, triage\n");
        var host = HostFile.Read(derived.Host!).Machines["assignment"];
        host.Triggers["Triage"]
            .Client!.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .Equal("triage");
        host.Triggers["SubmitFix"]
            .Client!.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .Equal("fixer");
        host.Start!.RoleSets.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(["triage", "fixer"]);
    }

    [Test]
    public async Task A_fixer_firing_triage_is_refused_on_the_host()
    {
        var derived = Derive(Lanes);
        var machine = IrMachines.Create(
            new IrMachines.Source(
                JsonNode.Parse(derived.Irs["assignment"])!.AsObject(),
                HostFile.Read(derived.Host!).Machines["assignment"]
            ),
            []
        );
        var rules = ((IMachineInternals)machine).Rules;
        var fay = new Actor.Client("jwt:fay", "fixer");
        var tess = new Actor.Client("jwt:tess", "triage");

        (await CallerRules.Admits(fay, rules.Events["Triage"], default)).Should().BeFalse();
        (await CallerRules.Admits(tess, rules.Events["Triage"], default)).Should().BeTrue();
        (await CallerRules.Admits(fay, rules.Events["SubmitFix"], default)).Should().BeTrue();
        (await CallerRules.Admits(tess, rules.Events["SubmitFix"], default)).Should().BeFalse();
        (await CallerRules.Admits(new("jwt:vic", "visitor"), rules.Start!, default))
            .Should()
            .BeFalse("a visitor owns no assignment");
        MachineRulesStartupValidator
            .Problems([machine], new StateMachineOptions(), null)
            .Result.Should()
            .BeEmpty();
    }

    [Test]
    public void A_rule_that_would_admit_a_denied_actor_is_a_problem()
    {
        var derived = Derive(
            Lanes,
            Glossary.Replace("\"roles\": [\"fixer\"]", "\"roles\": [\"fixer\", \"triage\"]")
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Fay may not Triage here")
            .And.Contain("admits a fixer too");
    }

    [Test]
    public void A_person_of_an_actor_that_owns_none_is_refused()
    {
        var derived = Derive(
            """
            Feature: Visiting
              Scenario: A visitor has an assignment
                Given Vic is a visitor
                And Vic has an assignment
                When Vic triages her assignment
                Then Vic's assignment is triaged
            """
        );

        derived.Problems.Should().NotBeEmpty();
        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("belongs to a triage bot or a fixer"));
    }

    private static Derived Derive(string feature, string glossary = Glossary) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("lanes.feature", feature)]
        );
}
