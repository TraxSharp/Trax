using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The IR exporter turns a declaratively-authored machine into the neutral IR. This verifies the IR carries
/// the declarative data faithfully (structure, per-state schema, guard rules, reducers) so a generator has
/// everything it needs, and that the output is deterministic (a stable golden).
/// </summary>
public class IrExporterTests
{
    private static JsonObject Ir() =>
        (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!;

    [Test]
    public void Export_is_deterministic()
    {
        IrExporter
            .Export(DeclarativeTurnstile.Built)
            .Should()
            .Be(IrExporter.Export(DeclarativeTurnstile.Built));
    }

    [Test]
    public void Export_carries_identity_and_structure()
    {
        var ir = Ir();

        ir["id"]!.GetValue<string>().Should().Be("turnstile");
        ir["version"]!.GetValue<int>().Should().Be(1);
        ir["initialState"]!.GetValue<string>().Should().Be("Locked");
        ir["states"]!
            .AsArray()
            .Select(n => n!.GetValue<string>())
            .Should()
            .Equal("Locked", "Unlocked");
        ir["triggers"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("Coin", "Push");
    }

    [Test]
    public void Export_carries_the_per_state_context_schema()
    {
        var unlocked = (JsonObject)Ir()["context"]!["Unlocked"]!;
        var field = (JsonObject)unlocked["fields"]!.AsArray().Single()!;

        field["name"]!.GetValue<string>().Should().Be("paidWith");
        field["type"]!.GetValue<string>().Should().Be("string");
        field["nullable"]!.GetValue<bool>().Should().BeFalse();
        // [MinLength(1)] became a non-empty constraint.
        var constraint = (JsonObject)field["constraints"]!.AsArray().Single()!;
        constraint["rule"]!.GetValue<string>().Should().Be("nonEmpty");
    }

    [Test]
    public void Export_carries_guards_and_reducers_as_data()
    {
        var coin = Ir()["transitions"]!
            .AsArray()
            .Select(t => (JsonObject)t!)
            .Single(t => t["trigger"]!.GetValue<string>() == "Coin");

        var guard = (JsonObject)coin["guard"]!;
        guard["rule"]!.GetValue<string>().Should().Be("oneOf");
        guard["source"]!.GetValue<string>().Should().Be("input");
        guard["field"]!.GetValue<string>().Should().Be("coin");
        guard["values"]!
            .AsArray()
            .Select(v => v!.GetValue<string>())
            .Should()
            .Equal("quarter", "dollar");
        coin["guardMessage"]!
            .GetValue<string>()
            .Should()
            .Be("Only a quarter or a dollar is accepted.");

        var reduce = (JsonObject)coin["reduce"]!;
        reduce["reduce"]!.GetValue<string>().Should().Be("set");
        var step = (JsonObject)reduce["steps"]!.AsArray().Single()!;
        step["field"]!.GetValue<string>().Should().Be("paidWith");
        ((JsonObject)step["value"]!)["input"]!.GetValue<string>().Should().Be("coin");
    }

    [Test]
    public void Export_matches_the_committed_ir_golden()
    {
        // The full byte-exact IR for the declarative turnstile: identity, structure, per-state context
        // schema, and per-transition guard/reducer as data, canonical (keys sorted). A change to the
        // authoring or the exporter must move this deliberately.
        const string golden =
            """{"committedStates":[],"context":{"Locked":{"fields":[]},"Unlocked":{"fields":[{"constraints":[{"field":"paidWith","rule":"nonEmpty","source":"context"}],"name":"paidWith","nullable":false,"type":"string"}]}},"id":"turnstile","initialContext":{},"initialState":"Locked","inputs":{"Coin":{"fields":[{"constraints":[],"name":"coin","nullable":false,"type":"string"}]}},"states":["Locked","Unlocked"],"transitions":[{"from":"Locked","guard":{"field":"coin","rule":"oneOf","source":"input","values":["quarter","dollar"]},"guardMessage":"Only a quarter or a dollar is accepted.","reduce":{"reduce":"set","steps":[{"field":"paidWith","value":{"input":"coin"}}]},"to":"Unlocked","trigger":"Coin"},{"from":"Unlocked","reduce":{"reduce":"clear"},"to":"Locked","trigger":"Push"}],"triggers":["Coin","Push"],"version":1}""";

        IrExporter.Export(DeclarativeTurnstile.Built).Should().Be(golden);
    }

    [Test]
    public void Export_carries_the_trigger_input_schema()
    {
        var coin = (JsonObject)Ir()["inputs"]!["Coin"]!;
        var field = (JsonObject)coin["fields"]!.AsArray().Single()!;

        field["name"]!.GetValue<string>().Should().Be("coin");
        field["type"]!.GetValue<string>().Should().Be("string");
    }

    [Test]
    public void Export_of_a_rich_machine_covers_every_rule_and_reducer_kind()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(RichExportMachine.Built))!;

        ir["id"]!.GetValue<string>().Should().Be("rich");
        ir["committedStates"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Contain("B");

        var transitions = ir["transitions"]!.AsArray().Select(t => (JsonObject)t!).ToList();

        // The Cst transition carries an effect (key derived from machine id + trigger), a custom guard, and
        // a custom reducer.
        var cst = transitions.Single(t => t["trigger"]!.GetValue<string>() == "Cst");
        ((JsonObject)cst["effect"]!)["keyPrefix"]!.GetValue<string>().Should().Be("rich:Cst");
        ((JsonObject)cst["guard"]!)["rule"]!.GetValue<string>().Should().Be("custom");
        ((JsonObject)cst["reduce"]!)["reduce"]!.GetValue<string>().Should().Be("custom");

        // The Set1 guard is an `all` containing every simple rule kind.
        var set1 = transitions.Single(t => t["trigger"]!.GetValue<string>() == "Set1");
        var kinds = ((JsonObject)set1["guard"]!)["rules"]!
            .AsArray()
            .Select(r => ((JsonObject)r!)["rule"]!.GetValue<string>())
            .ToList();
        kinds
            .Should()
            .Contain(
                new[]
                {
                    "present",
                    "absent",
                    "ofType",
                    "nonEmpty",
                    "oneOf",
                    "compare",
                    "count",
                    "any",
                }
            );

        // Every reducer kind appears across the transitions.
        var reduceKinds = transitions
            .Where(t => t["reduce"] is not null)
            .Select(t => ((JsonObject)t["reduce"]!)["reduce"]!.GetValue<string>())
            .ToList();
        reduceKinds.Should().Contain(new[] { "set", "clear", "reset", "keep", "custom" });
    }
}
