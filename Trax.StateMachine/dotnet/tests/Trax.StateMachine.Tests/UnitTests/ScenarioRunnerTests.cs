using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The runner runs features on derived machines in memory and answers as the host does (ADR
/// <c>Trax.Docs/adr/0058</c>): a trigger no client is served is not served, another owner's instance is
/// <c>NOT_FOUND</c>, a caller the rule does not admit is <c>FORBIDDEN</c> in the host's words, and a Given arranges the
/// instance without firing anything: it never fires a guard's trigger to make a Given hold, and never refuses with
/// words the host does not send.
/// </summary>
public class ScenarioRunnerTests
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
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Placed": "is placed", "Baking": "is baking" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Place": { "phrase": "places {subject}" },
                "Bake": { "phrase": "{subject} starts baking" },
                "Rush": { "phrase": "rushes {subject}" }
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

    private const string Ordering = """
        @ordering
        Feature: Ordering
          Background:
            Given Ada is a customer
            And Ada has an order

          @smoke
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

          @slow
          Scenario: Baking
            Given Ada's order is placed
            When Ada's order starts baking
            Then Ada's order is baking

          Scenario: Nobody rushes an order
            When Ada rushes her order
            Then Ada is not allowed
        """;

    private static readonly GlossaryActor Customer = new(
        "customer",
        "a customer",
        ["customer"],
        false
    );
    private static readonly Person Ada = new("Ada", Customer);
    private static readonly Person Bo = new("Bo", Customer);

    [Test]
    public void The_features_pass_on_the_machines_they_derived()
    {
        var report = Run(Ordering);

        report.Problems.Should().BeEmpty();
        report.Scenarios.Should().HaveCount(5);
        report.Scenarios.Should().OnlyContain(s => s.Status == ScenarioStatus.Passed);
    }

    [Test]
    public async Task A_trigger_no_client_is_served_is_not_served()
    {
        var driver = Driver();
        await driver.Start(Ada, "order");

        (await driver.Fire(Ada, Ada.Key(), "Rush", null)).Should().BeOfType<Answer.NotServed>();
        (await driver.Fire(Ada, Ada.Key(), "Bake", null))
            .Should()
            .BeOfType<Answer.NotServed>("the server fires Bake");
    }

    [Test]
    public async Task Another_owners_instance_is_not_found_in_the_hosts_words()
    {
        var driver = Driver();
        await driver.Start(Ada, "order");

        (await driver.Fire(Bo, Ada.Key(), "Place", null))
            .Should()
            .Be(new Answer.Refused(RefusalCode.NotFound, "No draft with that id."));
    }

    [Test]
    public void A_caller_the_committed_rule_does_not_admit_is_forbidden_in_the_hosts_words()
    {
        var (irs, host) = Derived();
        host["machines"]!["order"]!["triggers"]!["Place"] = new JsonObject
        {
            ["roles"] = new JsonArray(new JsonArray("manager")),
        };

        var report = Run(Ordering, irs, host);

        report
            .Scenarios.Single(s => s.Scenario.Name == "Placing an order with something in it")
            .Failure!.Message.Should()
            .Contain("FORBIDDEN \"You may not do this.\"");
    }

    [Test]
    public async Task A_guard_given_sets_the_context_and_fires_nothing()
    {
        var walk = Walk(
            """
            Feature: Ordering
              Scenario: Ready
                Given Ada is a customer
                And Ada has an order
                And Ada's order has something in it
                Then Ada's order is a draft
            """
        );
        var driver = Driver();

        var result = await ScenarioRunner.Run(walk, Features.Glossary.Read(Glossary), driver);

        result.Status.Should().Be(ScenarioStatus.Passed);
        var now = (await driver.Read(Ada.Key()))!.Value;
        now.State.Should().Be("Draft");
        now.Context["loaves"]!.GetValue<long>().Should().BeGreaterThan(0);
    }

    [Test]
    public void A_scenario_the_machines_were_not_derived_with_fails_naming_the_step()
    {
        var (irs, host) = Derived();

        var report = Run(
            """
            Feature: Rushing
              Scenario: Ada rushes
                Given Ada is a customer
                And Ada has an order
                When Ada rushes her order
                Then Ada's order is placed
            """,
            irs,
            host
        );

        var failure = report.Scenarios.Single().Failure!;
        failure.Step.Line.Should().Be(6);
        failure.Message.Should().Contain("not served");
    }

    [Test]
    public void A_run_selects_scenarios_as_cucumber_does()
    {
        Names(Run(Ordering, selection: new RunSelection(Tags: "@ordering and not @slow")))
            .Should()
            .NotContain("Baking")
            .And.HaveCount(4);
        Names(Run(Ordering, selection: new RunSelection(Tags: "@smoke or @slow")))
            .Should()
            .Equal("Ordering loaves", "Baking");
        Names(Run(Ordering, selection: new RunSelection(Names: ["^Placing"])))
            .Should()
            .Equal("Placing an order with something in it", "Placing an empty order");
        Names(Run(Ordering, selection: new RunSelection(Locations: [("ordering.feature", 12)])))
            .Should()
            .Equal("Placing an order with something in it");
    }

    [Test]
    public void A_dry_run_reads_every_step_and_runs_none()
    {
        var report = Run(
            Ordering
                + """

                  Scenario: Juggling
                    When Ada juggles her order
                    Then Ada's order is placed
                """,
            dryRun: true
        );

        report
            .Scenarios.Single(s => s.Scenario.Name == "Juggling")
            .Status.Should()
            .Be(ScenarioStatus.Undefined);
        report
            .Scenarios.Where(s => s.Scenario.Name != "Juggling")
            .SelectMany(s => s.Steps)
            .Should()
            .OnlyContain(s => s.Status == null);
    }

    [Test]
    public void Fail_fast_stops_at_the_first_scenario_that_does_not_pass()
    {
        var (irs, host) = Derived();
        host["machines"]!["order"]!["triggers"]!.AsObject().Remove("Order");

        var report = Run(Ordering, irs, host, failFast: true);

        report.Scenarios.Should().ContainSingle().Which.Status.Should().Be(ScenarioStatus.Failed);
    }

    [TestCase("@a and not (@b or @c)", new[] { "@a" }, true)]
    [TestCase("@a and not (@b or @c)", new[] { "@a", "@c" }, false)]
    [TestCase("not @a or @b and @c", new[] { "@a", "@b", "@c" }, true)]
    [TestCase("@with\\ space", new[] { "@with space" }, true)]
    public void Tag_expressions_read_as_cucumber_reads_them(
        string expression,
        string[] tags,
        bool matches
    ) => TagExpression.Parse(expression)(tags).Should().Be(matches);

    [TestCase("@a and")]
    [TestCase("(@a")]
    [TestCase("smoke")]
    public void A_tag_expression_that_does_not_parse_is_refused(string expression) =>
        FluentActions
            .Invoking(() => TagExpression.Parse(expression))
            .Should()
            .Throw<FormatException>();

    private static IEnumerable<string> Names(RunReport report) =>
        report.Scenarios.Select(s => s.Scenario.Name);

    private static (Dictionary<string, JsonObject> Irs, JsonObject Host) Derived()
    {
        var derived = FeatureDerivation.Derive(
            Features.Glossary.Read(Glossary),
            [("ordering.feature", Ordering)]
        );
        derived.Problems.Should().BeEmpty();
        return (
            derived.Irs.ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value)!.AsObject()),
            JsonNode.Parse(derived.Host!)!.AsObject()
        );
    }

    private static RunReport Run(
        string feature,
        Dictionary<string, JsonObject>? irs = null,
        JsonObject? host = null,
        RunSelection? selection = null,
        bool dryRun = false,
        bool failFast = false
    )
    {
        if (irs is null || host is null)
            (irs, host) = Derived();
        return FeatureRun.Run(
            Features.Glossary.Read(Glossary),
            [("ordering.feature", feature)],
            irs,
            HostFile.Read(host.ToJsonString()),
            selection ?? new RunSelection(),
            dryRun,
            failFast
        );
    }

    private static MemoryDriver Driver()
    {
        var (irs, host) = Derived();
        return new MemoryDriver(
            irs,
            HostFile.Read(host.ToJsonString()),
            Features.Glossary.Read(Glossary)
        );
    }

    private static Walk Walk(string feature)
    {
        var glossary = Features.Glossary.Read(Glossary);
        var (scenarios, problems) = FeatureFiles.Parse([("w.feature", feature)]);
        problems.Should().BeEmpty();
        return Gherkin.Walk.Run(scenarios.Single(), glossary, new StepResolver(glossary));
    }
}

internal static class PersonKeys
{
    internal static InstanceKey Key(this Person person) => new("order", person.Name);
}
