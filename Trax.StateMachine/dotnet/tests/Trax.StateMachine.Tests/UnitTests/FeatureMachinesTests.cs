using AwesomeAssertions;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The Gherkin package's public calls (ADR <c>Trax.Docs/adr/0058</c>): derive, run on what was derived, find what is
/// missing, and draw the board, each with typed results.
/// </summary>
public class FeatureMachinesTests
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
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": { "Place": { "phrase": "places {subject}" }, "Cancel": { "phrase": "cancels {subject}" } }
            }
          }
        }
        """;

    private static readonly FeatureFile[] Features =
    [
        new(
            "ordering.feature",
            """
            Feature: Ordering
              @smoke
              Scenario: Ada places her order
                Given Ada is a customer
                And Ada has an order
                When Ada places her order
                Then Ada's order is placed
            """
        ),
    ];

    [Test]
    public void Derive_then_run_on_what_was_derived()
    {
        var glossary = FeatureGlossary.Read(Glossary);

        var derived = FeatureMachines.Derive(glossary, Features);
        var report = FeatureMachines.Run(
            glossary,
            Features,
            derived,
            new FeatureRunOptions { Tags = "@smoke" }
        );

        derived.Succeeded.Should().BeTrue();
        derived.Irs.Keys.Should().Equal("order");
        report.Passed.Should().BeTrue();
        report.Scenarios.Single().Tags.Should().Equal("@smoke");
        report.Scenarios.Single().Steps.Should().OnlyContain(s => s.Status == FeatureStatus.Passed);
    }

    [Test]
    public void A_derivation_with_problems_has_nothing_to_run_on()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(
            glossary,
            [new FeatureFile("bad.feature", "Feature: B\n  Scenario: S\n    When Ada juggles\n")]
        );

        derived.Succeeded.Should().BeFalse();
        derived.Problems.Should().NotBeEmpty();
        FluentActions
            .Invoking(() => FeatureMachines.Run(glossary, Features, derived))
            .Should()
            .Throw<ArgumentException>();
    }

    [Test]
    public void Missing_and_the_board_are_data()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(glossary, Features);

        FeatureMachines
            .Missing(glossary, Features)
            .Should()
            .Contain(g => g.Kind == "trigger" && g.What == "Cancel" && g.Stub != null);
        var board = FeatureMachines.Board(derived.Irs, derived.Host!);
        board.Stickies.Should().Contain(s => s.Kind == "command" && s.Label == "Place");
        board
            .Stickies.Single(s => s.Kind == "command")
            .Scenarios.Single()
            .Should()
            .Be(new FeatureScenarioRef("ordering.feature", 3, "Ada places her order"));
    }

    [Test]
    public void A_state_only_a_timer_reaches_is_not_missing()
    {
        var glossary = FeatureGlossary.Read(
            Glossary.Replace(
                "\"Placed\": \"is placed\" }",
                "\"Placed\": \"is placed\", \"Ready\": \"is ready\" }"
            )
        );
        FeatureFile[] features =
        [
            Features[0],
            new(
                "ready.feature",
                """
                Feature: Ready
                  Scenario: A placed order is ready after a while
                    Given Ada is a customer
                    And Ada has an order
                    And Ada's order is placed
                    When 10 seconds pass
                    Then Ada's order is ready
                """
            ),
        ];

        FeatureMachines.Derive(glossary, features).Succeeded.Should().BeTrue();
        FeatureMachines
            .Missing(glossary, features)
            .Should()
            .NotContain(g => g.Kind == "state" && g.What == "Ready");
    }

    [Test]
    public void Timers_that_loop_on_their_own_are_refused_as_the_host_refuses_them()
    {
        var glossary = FeatureGlossary.Read(
            """
            {
              "version": 1,
              "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
              "machines": {
                "shop": {
                  "system": "shop/main",
                  "name": "the shop",
                  "initial": "Open",
                  "states": { "Open": "is open", "Firing": "is baking a batch" },
                  "triggers": { "Close": { "phrase": "the shop closes" } }
                }
              }
            }
            """
        );
        FeatureFile[] features =
        [
            new(
                "oven.feature",
                """
                Feature: Oven
                  Scenario: The oven fires on its own
                    Given the shop is open
                    When 8 seconds pass
                    Then the shop is baking a batch

                  Scenario: A batch ends on its own
                    Given the shop is baking a batch
                    When 4 seconds pass
                    Then the shop is open
                """
            ),
        ];

        var derived = FeatureMachines.Derive(glossary, features);

        derived.Succeeded.Should().BeFalse();
        derived.Irs.Should().BeEmpty();
        var loops = derived
            .Problems.Where(p => p.Message.Contains("form a cycle no event breaks"))
            .ToList();
        loops
            .Should()
            .HaveCount(2, "each timer of the loop is named at the scenario that showed it");
        loops.Should().OnlyContain(p => p.File == "oven.feature");
        loops.Select(p => p.Line).Should().OnlyHaveUniqueItems();
    }

    private const string CascadeGlossary = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed", "Baking": "is baking" },
              "triggers": {
                "Place": { "phrase": "places {subject}" },
                "Bake": { "phrase": "{subject} starts baking" }
              }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Open",
              "states": { "Open": "is open", "Firing": "is baking a batch" },
              "triggers": { "Batch": { "phrase": "the oven fires" } }
            }
          }
        }
        """;

    private static readonly FeatureFile[] CascadeFeatures =
    [
        new(
            "oven.feature",
            """
            Feature: Oven
              Scenario: An order placed while the shop is open goes straight in
                Given Ada is a customer
                And Ada has an order
                And the shop is open
                When Ada places her order
                Then Ada's order is placed
                And the shop is baking a batch
                And Ada's order is baking

              Scenario: The oven fires
                Given the shop is open
                When the oven fires
                Then the shop is baking a batch

              Scenario: An order goes in the oven
                Given Ada is a customer
                And Ada has an order
                And Ada's order is placed
                When Ada's order starts baking
                Then Ada's order is baking
            """
        ),
    ];

    [Test]
    public void A_timer_longer_than_a_timer_may_wait_is_refused_as_the_host_refuses_it()
    {
        var glossary = FeatureGlossary.Read(
            Glossary.Replace(
                "\"Placed\": \"is placed\" }",
                "\"Placed\": \"is placed\", \"Expired\": \"has expired\" }"
            )
        );
        FeatureFile[] features =
        [
            Features[0],
            new(
                "expiry.feature",
                """
                Feature: Expiry
                  Scenario: A placed order expires after a long while
                    Given Ada is a customer
                    And Ada has an order
                    And Ada's order is placed
                    When 400 days pass
                    Then Ada's order has expired
                """
            ),
        ];

        var derived = FeatureMachines.Derive(glossary, features);

        derived.Succeeded.Should().BeFalse();
        derived.Irs.Should().BeEmpty();
        derived
            .Problems.Should()
            .ContainSingle(p => p.Message.Contains("days a timer may wait"))
            .Which.Should()
            .Be(
                new FeatureProblem(
                    "expiry.feature",
                    7,
                    "order: the timer of Placed waits 400.00:00:00, longer than the 366 days a timer may "
                        + "wait (MachineTimers.MaxDuration)."
                )
            );
    }

    [Test]
    public void A_cascade_may_come_back_to_the_instance_that_set_it_off()
    {
        var glossary = FeatureGlossary.Read(CascadeGlossary);

        var derived = FeatureMachines.Derive(glossary, CascadeFeatures);

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        derived
            .Host.Should()
            .Contain("\"shape\":\"toSystem\"")
            .And.Contain("\"shape\":\"broadcast\"");
        var report = FeatureMachines.Run(glossary, CascadeFeatures, derived);
        report
            .Scenarios.Where(s => s.Status != FeatureStatus.Passed)
            .Select(s =>
                $"{s.Name}: {s.Steps.FirstOrDefault(x => x.Status == FeatureStatus.Failed)?.Message}"
            )
            .Should()
            .BeEmpty();
    }

    // Each runs on the machines the cascade features derive, after the order is placed and the shop's oven, firing on
    // it, puts the order in to bake: placed, then baking.
    [TestCase(
        "A state the cascade never entered",
        """
                Then Ada's order is placed
                And the shop is baking a batch
                And Ada's order is a draft
            """,
        "Ada's order is a draft"
    )]
    [TestCase(
        "Still, after the cascade moved it",
        """
                Then Ada's order is placed
                And the shop is still open
            """,
        "the shop is still open"
    )]
    [TestCase(
        "The cascade read backwards",
        """
                Then Ada's order is baking
                And the shop is baking a batch
                And Ada's order is placed
            """,
        "Ada's order is placed"
    )]
    [TestCase(
        "A cascade an earlier When set off",
        """
                Then Ada's order is placed
                And the shop is baking a batch
                And Ada's order is baking
                When Bo places his order
                Then Bo's order is placed
                And Ada's order is placed
            """,
        "Ada's order is placed"
    )]
    public void A_then_after_a_cascade_names_what_it_entered_in_order(
        string name,
        string thens,
        string failing
    )
    {
        var glossary = FeatureGlossary.Read(CascadeGlossary);
        var derived = FeatureMachines.Derive(glossary, CascadeFeatures);
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        FeatureFile probe = new(
            "probe.feature",
            $"""
            Feature: Probe
              Scenario: {name}
                Given Ada is a customer
                And Ada has an order
                And Bo is a customer
                And Bo has an order
                And the shop is open
                When Ada places her order
            {thens}
            """
        );

        var report = FeatureMachines.Run(glossary, [probe], derived);

        var scenario = report.Scenarios.Should().ContainSingle().Subject;
        scenario
            .Status.Should()
            .Be(
                FeatureStatus.Failed,
                string.Join("; ", scenario.Steps.Select(s => $"{s.Text} {s.Status} {s.Message}"))
            );
        scenario.Steps.Single(s => s.Status == FeatureStatus.Failed).Text.Should().EndWith(failing);
    }

    [Test]
    public void A_cascade_that_comes_back_to_a_system_instance_is_caused_by_the_step_before_it()
    {
        var glossary = FeatureGlossary.Read(
            """
            {
              "version": 1,
              "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
              "machines": {
                "order": {
                  "ownedBy": "customer",
                  "noun": "order",
                  "start": "has an order",
                  "initial": "Draft",
                  "states": { "Draft": "is a draft", "Placed": "is placed", "Baking": "is baking" },
                  "triggers": { "Bake": { "phrase": "{subject} starts baking" } }
                },
                "shop": {
                  "system": "shop/main",
                  "name": "the shop",
                  "initial": "Closed",
                  "states": { "Closed": "is closed", "Open": "is open", "Busy": "is busy" },
                  "triggers": {
                    "Open": { "phrase": "the shop opens" },
                    "Rush": { "phrase": "the shop gets busy" }
                  }
                }
              }
            }
            """
        );
        FeatureFile[] features =
        [
            new(
                "shop.feature",
                """
                Feature: Shop
                  Scenario: Opening bakes the waiting orders, which keeps the shop busy
                    Given Ada is a customer
                    And Ada has an order
                    And Ada's order is placed
                    And the shop is closed
                    When the shop opens
                    Then the shop is open
                    And Ada's order is baking
                    And the shop is busy

                  Scenario: The shop gets busy
                    Given the shop is open
                    When the shop gets busy
                    Then the shop is busy

                  Scenario: An order goes in the oven
                    Given Ada is a customer
                    And Ada has an order
                    And Ada's order is placed
                    When Ada's order starts baking
                    Then Ada's order is baking
                """
            ),
        ];

        var derived = FeatureMachines.Derive(glossary, features);

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        var policies = Trax
            .StateMachine.Features.HostFile.Read(derived.Host!)
            .Policies.Select(p => $"{p.Shape} {p.Id}")
            .ToList();
        policies
            .Should()
            .BeEquivalentTo(["broadcast shop.Open>order.Bake", "toSystem order.Baking>shop.Rush"]);
        var report = FeatureMachines.Run(glossary, features, derived);
        report.Scenarios.Should().OnlyContain(s => s.Status == FeatureStatus.Passed);
    }

    [Test]
    public void An_ir_that_is_not_json_is_a_format_error_naming_the_machine()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(glossary, Features);
        var irs = new Dictionary<string, string> { ["order"] = "{ not json" };

        FluentActions
            .Invoking(() => FeatureMachines.Run(glossary, Features, irs, derived.Host!))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*'order'*");
        FluentActions
            .Invoking(() => FeatureMachines.Board(irs, derived.Host!))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*'order'*");
    }

    [Test]
    public void A_host_machine_with_no_ir_is_refused_before_any_scenario_runs()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(glossary, Features);
        var none = new Dictionary<string, string>();

        FluentActions
            .Invoking(() => FeatureMachines.Run(glossary, Features, none, derived.Host!))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*serves 'order'*");
        FluentActions
            .Invoking(() => FeatureMachines.Board(none, derived.Host!))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*serves 'order'*");
    }

    [Test]
    public void An_ir_the_host_file_was_not_derived_from_is_refused()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(glossary, Features);
        var ir = System.Text.Json.Nodes.JsonNode.Parse(derived.Irs["order"])!.AsObject();
        ir["description"] = "edited by hand";
        var irs = new Dictionary<string, string> { ["order"] = ir.ToJsonString() };

        FluentActions
            .Invoking(() => FeatureMachines.Run(glossary, Features, irs, derived.Host!))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*derived from another IR of 'order'*");
    }

    [Test]
    public void A_name_pattern_that_is_not_a_regular_expression_is_an_argument_error()
    {
        var glossary = FeatureGlossary.Read(Glossary);
        var derived = FeatureMachines.Derive(glossary, Features);

        FluentActions
            .Invoking(() =>
                FeatureMachines.Run(
                    glossary,
                    Features,
                    derived,
                    new FeatureRunOptions { Names = ["(unclosed"] }
                )
            )
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*'(unclosed'*")
            .Which.ParamName.Should()
            .Be("options");
    }

    [Test]
    public void A_glossary_that_is_not_one_is_refused_naming_its_key() =>
        FluentActions
            .Invoking(() => FeatureGlossary.Read("""{ "version": 2 }"""))
            .Should()
            .Throw<FormatException>()
            .WithMessage("*version*");
}
