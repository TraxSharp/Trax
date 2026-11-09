using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Machines;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine explain</c> turns derivation's provenance round (ADR <c>Trax.Docs/adr/0058</c>): each scenario with
/// the edges, timers, policies and grants it gave, a filter by name or file:line, and features that do not derive as a
/// message and exit 1.
/// </summary>
public class MachineExplainTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "customer": { "noun": "a customer", "roles": ["customer"] },
            "baker": { "noun": "a baker", "roles": ["baker"] }
          },
          "machines": {
            "shift": {
              "ownedBy": "baker",
              "noun": "shift",
              "start": "has a shift",
              "initial": "Off",
              "states": { "Off": "is not started", "On": "is on" },
              "triggers": { "ClockIn": { "phrase": "clocks in for {subject}" } }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open", "Firing": "is baking a batch" },
              "triggers": { "Open": { "phrase": "the shop opens" }, "Batch": { "phrase": "the oven fires" } }
            }
          }
        }
        """;

    private const string Shop = """
        Feature: The shop
          Scenario: A baker clocking in opens the shop
            Given Kit is a baker
            And Kit has a shift
            And the shop is closed
            When Kit clocks in for his shift
            Then Kit's shift is on
            And the shop is open

          Scenario: The oven fires
            Given the shop is open
            When the oven fires
            Then the shop is baking a batch

          Scenario: A batch takes four seconds
            Given the shop is baking a batch
            When 4 seconds pass
            Then the shop is open

          Scenario: The shop opens
            Given the shop is closed
            When the shop opens
            Then the shop is open

          Scenario: Opening an open shop changes nothing
            Given the shop is open
            When the shop opens
            Then the shop is open
        """;

    private string _dir = null!;
    private string Features => Path.Combine(_dir, "features");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-explain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Features);
        File.WriteAllText(Path.Combine(Features, "glossary.json"), Glossary);
        File.WriteAllText(Path.Combine(Features, "shop.feature"), Shop);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private (int Exit, string Out, string Err) Run(string? scenario = null)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = MachineCommand.RunExplain(
            Features,
            Path.Combine(Features, "glossary.json"),
            scenario,
            stdout,
            stderr
        );
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Test]
    public void Each_scenario_lists_the_edges_timers_policies_and_grants_it_gave()
    {
        var (exit, output, err) = Run();

        exit.Should().Be(0, err);
        output.Should().Contain("shop.feature:2 A baker clocking in opens the shop");
        output.Should().Contain("edge: shift Off --ClockIn--> On");
        output.Should().Contain("grant: shift ClockIn is served to baker");
        output
            .Should()
            .Contain("policy: when shift enters On, the server fires Open on shop/main (toSystem)");
        output.Should().Contain("timer: shop leaves Firing after 4 s for Open");
        output.Should().Contain("edge: shop Open --Open--> Open (a self-transition");
    }

    [Test]
    public void A_rule_is_every_role_set_and_any_one_role_within_a_set()
    {
        File.WriteAllText(
            Path.Combine(Features, "glossary.json"),
            Glossary.Replace("\"roles\": [\"baker\"]", "\"roles\": [\"baker\", \"staff\"]")
        );

        var (exit, output, err) = Run();

        exit.Should().Be(0, err);
        output.Should().Contain("grant: shift ClockIn is served to baker or staff");
        ScenarioExplanation
            .Rule(JsonNode.Parse("""{ "roles": [["customer", "baker"], ["staff"]] }"""))
            .Should()
            .Be("customer or baker and staff");
    }

    [Test]
    public void A_filter_keeps_the_scenarios_whose_name_or_place_matches()
    {
        var byName = Run("OVEN FIRES");
        byName.Exit.Should().Be(0, byName.Err);
        byName.Out.Should().Contain("The oven fires").And.NotContain("clocking in");

        var byPlace = Run("shop.feature:2");
        byPlace.Out.Should().Contain("clocking in").And.NotContain("The oven fires");

        var none = Run("no such scenario");
        none.Exit.Should().Be(1);
        none.Err.Should().Contain("No scenario");
    }

    [Test]
    public void Features_that_do_not_derive_are_their_problems_and_exit_1()
    {
        File.WriteAllText(
            Path.Combine(Features, "bad.feature"),
            "Feature: Bad\n  Scenario: Kit juggles\n    When Kit juggles\n"
        );

        var (exit, output, err) = Run();

        exit.Should().Be(1);
        output.Should().BeEmpty();
        err.Should().Contain("bad.feature").And.Contain("nothing to explain");
    }
}
