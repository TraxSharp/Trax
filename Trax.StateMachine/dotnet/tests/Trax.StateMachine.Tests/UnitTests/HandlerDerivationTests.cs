using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A glossary names the C# behind a machine (ADR <c>Trax.Docs/adr/0058</c>): a trigger's run-once effect, its custom
/// rules and reductions, and its migrations. Derivation writes what clients see into the IR (an effect's key prefix,
/// the custom names) and the types into the host file, and an in-memory run answers with the glossary's examples, so
/// the StateMachine sample's checkout written as features derives exactly the IR its class exports.
/// </summary>
public class HandlerDerivationTests
{
    internal const string Glossary = """
        {
          "version": 1,
          "actors": { "shopper": { "noun": "a shopper", "anyAuthenticated": true } },
          "machines": {
            "checkout": {
              "ownedBy": "shopper",
              "noun": "checkout",
              "start": "has a checkout",
              "version": 2,
              "initial": "Cart",
              "context": [
                {
                  "name": "items", "type": "array", "nullable": false,
                  "constraints": [ { "rule": "arrayOf", "source": "context", "field": "items", "type": "string" } ],
                  "values": { "type": "string", "nullable": false }
                },
                { "name": "receipt", "type": "string", "nullable": true, "constraints": [] },
                { "name": "total", "type": "integer", "nullable": false, "constraints": [], "minimum": -9223372036854775000, "maximum": 9223372036854775000 }
              ],
              "states": {
                "Cart": {
                  "phrase": "is in the cart",
                  "requires": { "rule": "all", "rules": [
                    { "rule": "absent", "source": "context", "field": "receipt" },
                    { "rule": "custom", "name": "checkout:total-matches-items" } ] }
                },
                "Review": {
                  "phrase": "is in review",
                  "requires": { "rule": "all", "rules": [
                    { "rule": "count", "source": "context", "field": "items", "op": "gt", "value": 0 },
                    { "rule": "absent", "source": "context", "field": "receipt" },
                    { "rule": "custom", "name": "checkout:total-matches-items" } ] }
                },
                "Paid": {
                  "phrase": "is paid",
                  "committed": true,
                  "requires": { "rule": "all", "rules": [
                    { "rule": "count", "source": "context", "field": "items", "op": "gt", "value": 0 },
                    { "rule": "nonEmpty", "source": "context", "field": "receipt" },
                    { "rule": "custom", "name": "checkout:total-matches-items" } ] },
                  "context": [
                    {
                      "name": "items", "type": "array", "nullable": false,
                      "constraints": [ { "rule": "arrayOf", "source": "context", "field": "items", "type": "string" } ],
                      "values": { "type": "string", "nullable": false }
                    },
                    { "name": "receipt", "type": "string", "nullable": false, "constraints": [] },
                    { "name": "total", "type": "integer", "nullable": false, "constraints": [], "minimum": -9223372036854775000, "maximum": 9223372036854775000 }
                  ]
                }
              },
              "triggers": {
                "AddItem": {
                  "phrase": "adds {item} to {subject}",
                  "input": [ { "name": "item", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "custom", "name": "checkout:add-item" }
                },
                "Next": { "phrase": "reviews {subject}" },
                "Back": { "phrase": "goes back from {subject}" },
                "Pay": {
                  "phrase": "pays for {subject}",
                  "input": [ { "name": "receipt", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "receipt", "value": { "input": "receipt" } } ] },
                  "guard": "payable",
                  "effect": { "type": "Trax.Samples.StateMachine.ICharge", "keyPrefix": "checkout:charge", "example": "rcpt-example" }
                },
                "Reset": { "phrase": "starts over with {subject}", "reduce": { "reduce": "reset" } }
              },
              "guards": {
                "named": {
                  "holds": "names the item",
                  "fails": "names no item",
                  "rule": { "rule": "nonEmpty", "source": "input", "field": "item" }
                },
                "hasItems": {
                  "holds": "has something in it",
                  "fails": "has nothing in it",
                  "rule": { "rule": "count", "source": "context", "field": "items", "op": "gt", "value": 0 }
                },
                "payable": {
                  "holds": "can be paid",
                  "fails": "cannot be paid",
                  "rule": { "rule": "all", "rules": [
                    { "rule": "count", "source": "context", "field": "items", "op": "gt", "value": 0 },
                    { "rule": "nonEmpty", "source": "input", "field": "receipt" } ] }
                }
              },
              "rules": {
                "checkout:total-matches-items": { "type": "Trax.Samples.StateMachine.TotalMatchesItems", "example": true }
              },
              "reductions": {
                "checkout:add-item": { "type": "Trax.Samples.StateMachine.AddItemToCart", "example": { "items": ["bread"], "total": 999 } }
              },
              "migrations": { "1": "Trax.Samples.StateMachine.CheckoutFromVersion1" }
            }
          }
        }
        """;

    internal const string Features = """
        Feature: Checking out
          Background:
            Given Ada is a shopper
            And Ada has a checkout

          Scenario: Adding an item
            When Ada adds "bread" to her checkout
            Then Ada's checkout is in the cart

          Scenario: Adding nothing
            When Ada adds "" to her checkout
            Then Ada is told "Name the item to add."
            And Ada's checkout is still in the cart

          Scenario: Reviewing an empty cart
            When Ada reviews her checkout
            Then Ada is told "Add an item before reviewing."
            And Ada's checkout is still in the cart

          Scenario: Going back to the cart
            When Ada adds "bread" to her checkout
            Then Ada's checkout is in the cart
            When Ada reviews her checkout
            Then Ada's checkout is in review
            When Ada goes back from her checkout
            Then Ada's checkout is in the cart

          Scenario: Paying, and starting over
            When Ada adds "bread" to her checkout
            Then Ada's checkout is in the cart
            When Ada reviews her checkout
            Then Ada's checkout is in review
            When Ada pays for her checkout
            Then Ada's checkout is paid
            When Ada starts over with her checkout
            Then Ada's checkout is in the cart

          Scenario: Paying for nothing
            Given Ada's checkout is in review
            When Ada pays for her checkout
            Then Ada is told "Checkout needs items and a receipt to be paid."
            And Ada's checkout is still in review

          @host
          Scenario: Two items cost twice one
            When Ada adds "bread" to her checkout
            Then Ada's checkout is in the cart
            When Ada adds "jam" to her checkout
            Then Ada's checkout is in the cart
        """;

    [Test]
    public void The_sample_checkout_written_as_features_derives_exactly_the_IR_its_class_exports()
    {
        var derived = Derive(Glossary, Features);

        derived.Problems.Should().BeEmpty();
        derived.Irs["checkout"].Should().Be(FluentCheckout.Export());
    }

    [Test]
    public void The_host_file_names_each_effect_handler_and_migration_the_IR_uses_and_nothing_else()
    {
        var derived = Derive(Glossary, Features);

        var entry = JsonNode.Parse(derived.Host!)!["machines"]!["checkout"]!;
        entry["effects"]!
            .ToJsonString()
            .Should()
            .Be("""{"Pay":"Trax.Samples.StateMachine.ICharge"}""");
        entry["handlers"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"reductions":{"checkout:add-item":"Trax.Samples.StateMachine.AddItemToCart"},"rules":{"checkout:total-matches-items":"Trax.Samples.StateMachine.TotalMatchesItems"}}"""
            );
        entry["migrations"]!
            .ToJsonString()
            .Should()
            .Be("""{"1":"Trax.Samples.StateMachine.CheckoutFromVersion1"}""");
        HostFile.Read(derived.Host!).Machines["checkout"].Effects.Should().ContainKey("Pay");
    }

    [Test]
    public void An_in_memory_run_plays_the_examples_and_skips_a_scenario_only_the_host_runs()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var files = new[] { new FeatureFile("checkout.feature", Features) };
        var derived = FeatureMachines.Derive(glossary, files);

        var report = FeatureMachines.Run(glossary, files, derived);

        report
            .Passed.Should()
            .BeTrue(string.Join("\n", report.Scenarios.Select(s => $"{s.Name}: {s.Status}")));
        report
            .Scenarios.Single(s => s.Name == "Two items cost twice one")
            .Status.Should()
            .Be(FeatureStatus.Skipped);
        report
            .Scenarios.Where(s => s.Name != "Two items cost twice one")
            .Should()
            .OnlyContain(s => s.Status == FeatureStatus.Passed);
    }

    [Test]
    public void A_scenario_only_the_host_runs_still_has_to_read()
    {
        var derived = Derive(
            Glossary,
            Features.Replace(
                "When Ada adds \"jam\" to her checkout",
                "When Ada adds jam to her checkout"
            )
        );

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("adds jam");
    }

    [TestCase(
        "\"rules\": {",
        "\"rules\": { \"spare\": { \"type\": \"A.B\", \"example\": true },",
        "*spare*nothing in the machine names*"
    )]
    [TestCase(
        "\"checkout:add-item\": { \"type\": \"Trax.Samples.StateMachine.AddItemToCart\", \"example\": { \"items\": [\"bread\"], \"total\": 999 } }",
        "\"other\": { \"type\": \"A.B\", \"example\": {} }",
        "*has no 'checkout:add-item'*"
    )]
    [TestCase(
        "\"migrations\": { \"1\": \"Trax.Samples.StateMachine.CheckoutFromVersion1\" }",
        "\"migrations\": {}",
        "*none from version 1*"
    )]
    [TestCase(
        "\"migrations\": { \"1\":",
        "\"migrations\": { \"2\": \"A.B\", \"1\":",
        "*steps from version 2*"
    )]
    [TestCase(
        "\"phrase\": \"pays for {subject}\"",
        "\"phrase\": \"pays {receipt} for {subject}\"",
        "*no input of its own*"
    )]
    [TestCase(
        "\"Next\": { \"phrase\": \"reviews {subject}\" }",
        "\"Next\": { \"phrase\": \"reviews {subject}\", \"effect\": { \"type\": \"A.B\", \"example\": \"r\" } }",
        "*second effect*"
    )]
    public void A_glossary_whose_handlers_do_not_pair_with_what_it_names_is_refused(
        string from,
        string to,
        string message
    )
    {
        Glossary.Should().Contain(from);
        var act = () => Trax.StateMachine.Features.Glossary.Read(Glossary.Replace(from, to));

        act.Should().Throw<FormatException>().WithMessage(message);
    }

    [Test]
    public void A_host_file_that_does_not_pair_its_handlers_with_the_IR_is_refused_when_served()
    {
        var derived = Derive(Glossary, Features);
        var ir = JsonNode.Parse(derived.Irs["checkout"])!.AsObject();
        var host = HostFile.Read(derived.Host!).Machines["checkout"];

        foreach (
            var (broken, expected) in new[]
            {
                (
                    host with
                    {
                        Effects = new Dictionary<string, string>(),
                    },
                    "*runs an effect on 'Pay'*"
                ),
                (
                    host with
                    {
                        Rules = new Dictionary<string, string>(host.Rules) { ["extra"] = "A.B" },
                    },
                    "*rule type for 'extra'*"
                ),
                (host with { Migrations = new Dictionary<int, string>() }, "*version 2*'1'*"),
            }
        )
        {
            var act = () =>
                IrMachines.Create(
                    new IrMachines.Source(ir, broken),
                    [typeof(HandlerDerivationTests).Assembly]
                );
            act.Should().Throw<InvalidOperationException>().WithMessage(expected);
        }
    }

    // The StateMachine sample's checkout, written in C# as the sample writes it.
    private enum State
    {
        Cart,
        Review,
        Paid,
    }

    private enum Trigger
    {
        Next,
        Back,
        Pay,
        Reset,
        AddItem,
    }

    private sealed record CheckoutContext
    {
        public List<string> Items { get; init; } = [];
        public string? Receipt { get; init; }
        public long Total { get; init; }
    }

    private sealed record PaidContext
    {
        public List<string> Items { get; init; } = [];
        public string Receipt { get; init; } = "";
        public long Total { get; init; }
    }

    private sealed record ItemInput
    {
        public string Item { get; init; } = "";
    }

    private sealed record PayInput
    {
        public string Receipt { get; init; } = "";
    }

    private interface ICharge : ISnapshotEffect;

    private static class FluentCheckout
    {
        private static readonly Rule HasItems = Field((CheckoutContext c) => c.Items)
            .CountGreaterThan(0);
        private static readonly Rule Priced = new Rule.Custom("checkout:total-matches-items");

        internal static string Export()
        {
            var m = new MachineBuilder<State, Trigger>();
            m.Id("checkout")
                .Version(2)
                .StartsAt(
                    State.Cart,
                    () =>
                        new JsonObject
                        {
                            ["items"] = new JsonArray(),
                            ["receipt"] = null,
                            ["total"] = 0,
                        }
                )
                .MigrateFrom(1, (state, ctx) => new MigrationResult(state, ctx))
                .CustomGuard("checkout:total-matches-items", (_, _) => true)
                .CustomReducer("checkout:add-item", (ctx, _) => ctx);
            m.In(State.Cart)
                .Context<CheckoutContext>()
                .Requires(All(Field((CheckoutContext c) => c.Receipt).Absent(), Priced))
                .On(Trigger.AddItem)
                .WithInput<ItemInput>()
                .When(Input((ItemInput i) => i.Item).NonEmpty())
                .Because("Name the item to add.")
                .Reduce(new Reduction.Custom("checkout:add-item"))
                .To(State.Cart)
                .On(Trigger.Next)
                .When(HasItems)
                .Because("Add an item before reviewing.")
                .To(State.Review);
            m.In(State.Review)
                .Context<CheckoutContext>()
                .Requires(All(HasItems, Field((CheckoutContext c) => c.Receipt).Absent(), Priced))
                .On(Trigger.Back)
                .To(State.Cart)
                .On(Trigger.Pay)
                .WithInput<PayInput>()
                .When(All(HasItems, Input((PayInput i) => i.Receipt).NonEmpty()))
                .Because("Checkout needs items and a receipt to be paid.")
                .RunsOnce<ICharge>("checkout:charge")
                .Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
                .To(State.Paid);
            m.In(State.Paid)
                .Context<PaidContext>()
                .Committed()
                .Requires(
                    All(
                        Field((PaidContext c) => c.Items).CountGreaterThan(0),
                        Field((PaidContext c) => c.Receipt).NonEmpty(),
                        Priced
                    )
                )
                .On(Trigger.Reset)
                .Reduce(Reset())
                .To(State.Cart);
            return IrExporter.Export(m.Build());
        }
    }

    private static Derived Derive(string glossary, string features) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("checkout.feature", features)]
        );
}
