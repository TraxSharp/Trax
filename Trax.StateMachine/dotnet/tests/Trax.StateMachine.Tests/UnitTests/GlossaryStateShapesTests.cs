using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Tests.Fakes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A glossary says a machine's version, each state's shape (its phrase, committed, the rule its context meets, a context
/// of its own), the context a new instance starts with, and whether the IR carries a differential block (ADR
/// <c>Trax.Docs/adr/0058</c>). Derivation writes each into the IR as the fluent exporter writes it, so a machine
/// written in C# and the same machine written as features export the same file.
/// </summary>
public class GlossaryStateShapesTests
{
    private const string Receipts = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "anyAuthenticated": true } },
          "machines": {
            "checkout": {
              "ownedBy": "customer",
              "noun": "checkout",
              "start": "has a checkout",
              "version": 3,
              "migrations": { "1": "Trax.StateMachine.Tests.UnitTests.KeepsTheSnapshot", "2": "Trax.StateMachine.Tests.UnitTests.KeepsTheSnapshot" },
              "initial": "Cart",
              "initialContext": { "receipt": null },
              "context": [ { "name": "receipt", "type": "string", "nullable": true, "constraints": [] } ],
              "states": {
                "Cart": "is in the cart",
                "Paid": {
                  "phrase": "is paid",
                  "committed": true,
                  "requires": { "rule": "nonEmpty", "source": "context", "field": "receipt" },
                  "context": [ { "name": "receipt", "type": "string", "nullable": false, "constraints": [] } ]
                }
              },
              "triggers": {
                "Pay": {
                  "phrase": "pays {receipt} for {subject}",
                  "input": [ { "name": "receipt", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "receipt", "value": { "input": "receipt" } } ] }
                }
              }
            }
          }
        }
        """;

    private const string Paying = """
        Feature: Paying
          Scenario: Paying with a receipt
            Given Ada is a customer
            And Ada has a checkout
            When Ada pays "r-1" for her checkout
            Then Ada's checkout is paid
        """;

    [Test]
    public void A_committed_state_with_a_rule_and_its_own_context_is_written_into_the_IR_and_served_as_written()
    {
        var derived = Derive(Receipts, ("paying.feature", Paying));

        derived.Problems.Should().BeEmpty();
        var ir = JsonNode.Parse(derived.Irs["checkout"])!.AsObject();
        ir["version"]!.GetValue<int>().Should().Be(3);
        ir["committedStates"]!.ToJsonString().Should().Be("""["Paid"]""");
        ir["invariants"]!["Paid"]!["rule"]!.GetValue<string>().Should().Be("nonEmpty");
        ir["invariants"]!.AsObject().ContainsKey("Cart").Should().BeFalse();
        ir["initialContext"]!.ToJsonString().Should().Be("""{"receipt":null}""");
        ir["context"]!["Paid"]!["fields"]![0]!["nullable"]!.GetValue<bool>().Should().BeFalse();
        ir["context"]!["Cart"]!["fields"]![0]!["nullable"]!.GetValue<bool>().Should().BeTrue();

        var host = HostFile.Read(derived.Host!);
        IrMachines
            .Create(
                new IrMachines.Source(ir, host.Machines["checkout"]),
                [typeof(KeepsTheSnapshot).Assembly]
            )
            .ExportIr()
            .Should()
            .Be(
                derived.Irs["checkout"],
                "the host replays the committed state and its rule as written"
            );

        // The rule holds the state: a snapshot in Paid with no receipt is not one the engine takes.
        var engine = new IrMachine(ir);
        engine
            .Rehydrate(
                """{"machine":"checkout","version":3,"state":"Paid","context":{"receipt":""}}"""
            )
            .Should()
            .BeOfType<RehydrationResult.Error>();
        engine
            .Rehydrate(
                """{"machine":"checkout","version":3,"state":"Paid","context":{"receipt":"r-1"}}"""
            )
            .Should()
            .BeOfType<RehydrationResult.Ok>();
    }

    [Test]
    public void A_state_shape_with_a_key_the_glossary_does_not_have_is_refused()
    {
        var act = () =>
            Glossary.Read(
                Receipts.Replace("\"committed\": true,", "\"committed\": true, \"final\": true,")
            );

        act.Should().Throw<FormatException>().WithMessage("*/machines/checkout/states/Paid*final*");
    }

    // The StateMachine sample's turnstile, written in C# as the sample writes it.
    private sealed record UnlockedContext
    {
        [MinLength(1)]
        public string PaidWith { get; init; } = "";
    }

    private sealed record CoinInput
    {
        public string Coin { get; init; } = "";
    }

    private static string FluentTurnstile()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);
        return IrExporter.Export(m.Build());
    }

    private const string TurnstileGlossary = """
        {
          "version": 1,
          "actors": { "rider": { "noun": "a rider", "anyAuthenticated": true } },
          "machines": {
            "turnstile": {
              "ownedBy": "rider",
              "noun": "turnstile",
              "start": "has a turnstile",
              "initial": "Locked",
              "states": {
                "Locked": { "phrase": "is locked", "context": [] },
                "Unlocked": {
                  "phrase": "is unlocked",
                  "context": [
                    {
                      "name": "paidWith", "type": "string", "nullable": false,
                      "constraints": [ { "rule": "nonEmpty", "source": "context", "field": "paidWith" } ]
                    }
                  ]
                }
              },
              "triggers": {
                "Coin": {
                  "phrase": "puts {coin} in {subject}",
                  "input": [ { "name": "coin", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "paidWith", "value": { "input": "coin" } } ] }
                },
                "Push": { "phrase": "pushes through {subject}", "reduce": { "reduce": "clear" } }
              },
              "guards": {
                "accepted": {
                  "holds": "takes the coin",
                  "fails": "refuses the coin",
                  "rule": { "rule": "oneOf", "source": "input", "field": "coin", "values": ["quarter", "dollar"] }
                }
              }
            }
          }
        }
        """;

    private const string Turnstile = """
        Feature: The turnstile
          Background:
            Given Ada is a rider
            And Ada has a turnstile

          Scenario: A quarter unlocks it
            When Ada puts "quarter" in her turnstile
            Then Ada's turnstile is unlocked

          Scenario: A button does not
            When Ada puts "button" in her turnstile
            Then Ada is told "Only a quarter or a dollar is accepted."
            And Ada's turnstile is still locked

          Scenario: Pushing through locks it again
            When Ada puts "dollar" in her turnstile
            Then Ada's turnstile is unlocked
            When Ada pushes through her turnstile
            Then Ada's turnstile is locked
        """;

    [Test]
    public void The_sample_turnstile_written_as_features_derives_exactly_the_IR_its_class_exports()
    {
        var derived = Derive(TurnstileGlossary, ("turnstile.feature", Turnstile));

        derived.Problems.Should().BeEmpty();
        derived.Irs["turnstile"].Should().Be(FluentTurnstile());
    }

    [Test]
    public void A_differential_block_is_derived_from_what_the_scenarios_sent_and_set_up()
    {
        var glossary = TurnstileGlossary.Replace(
            "\"initial\": \"Locked\",",
            "\"initial\": \"Locked\", \"differential\": true,"
        );
        var features =
            Turnstile
            + """


                  Scenario: Pushing an unlocked turnstile it was left in
                    Given Ada's turnstile is unlocked
                    When Ada pushes through her turnstile
                    Then Ada's turnstile is locked
                """;

        var derived = Derive(glossary, ("turnstile.feature", features));

        derived.Problems.Should().BeEmpty();
        var ir = JsonNode.Parse(derived.Irs["turnstile"])!.AsObject();
        ir["differential"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"samples":{"Coin":[{"coin":"button"},{"coin":"dollar"},{"coin":"quarter"}]},"seeds":{"Unlocked":{}}}""",
                "each input once, in canonical order, and the context the Given set Unlocked up with"
            );

        // The host replays the block into the builder the exporter reads it back from.
        var host = HostFile.Read(derived.Host!);
        IrMachines
            .Create(new IrMachines.Source(ir, host.Machines["turnstile"]), [])
            .ExportIr()
            .Should()
            .Be(derived.Irs["turnstile"]);
    }

    [Test]
    public void A_differential_block_with_nothing_in_it_is_a_problem()
    {
        var derived = Derive(
            """
            {
              "version": 1,
              "actors": { "member": { "noun": "a member", "anyAuthenticated": true } },
              "machines": {
                "note": {
                  "ownedBy": "member",
                  "noun": "note",
                  "start": "has a note",
                  "initial": "Open",
                  "differential": true,
                  "states": { "Open": "is open", "Closed": "is closed" },
                  "triggers": { "Close": { "phrase": "closes {subject}" } }
                }
              }
            }
            """,
            (
                "notes.feature",
                """
                Feature: Notes
                  Scenario: Closing a note
                    Given Ada is a member
                    And Ada has a note
                    When Ada closes her note
                    Then Ada's note is closed
                """
            )
        );

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("differential");
    }

    private static Derived Derive(string glossary, params (string, string)[] features) =>
        FeatureDerivation.Derive(Glossary.Read(glossary), features);
}

/// <summary>A migration that keeps the snapshot as it was, for a machine whose versions differ in nothing stored.</summary>
public sealed class KeepsTheSnapshot : IMachineMigration
{
    public MigrationResult Migrate(string state, JsonObject context) => new(state, context);
}
