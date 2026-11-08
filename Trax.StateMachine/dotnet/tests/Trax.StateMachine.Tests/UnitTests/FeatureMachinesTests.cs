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
