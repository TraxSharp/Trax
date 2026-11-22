using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The IR exporter turns a declaratively-authored machine into the neutral IR. This verifies the IR carries
/// the declarative data faithfully (structure, per-state schema, guard rules, reducers) so a generator has
/// everything it needs, and that the output is deterministic (a stable golden).
/// </summary>
public class IrExporterTests
{
    private static JsonObject Ir() =>
        (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built)).NotNull();

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

        ir["id"].NotNull().GetValue<string>().Should().Be("turnstile");
        ir["version"].NotNull().GetValue<int>().Should().Be(1);
        ir["initialState"].NotNull().GetValue<string>().Should().Be("Locked");
        ir["states"]
            .NotNull()
            .AsArray()
            .Select(n => n.NotNull().GetValue<string>())
            .Should()
            .Equal("Locked", "Unlocked");
        ir["triggers"]
            .NotNull()
            .AsArray()
            .Select(n => n.NotNull().GetValue<string>())
            .Should()
            .Equal("Coin", "Push");
    }

    [Test]
    public void Export_carries_the_per_state_context_schema()
    {
        var unlocked = (JsonObject)Ir()["context"].NotNull()["Unlocked"].NotNull();
        var field = (JsonObject)unlocked["fields"].NotNull().AsArray().Single().NotNull();

        field["name"].NotNull().GetValue<string>().Should().Be("paidWith");
        field["type"].NotNull().GetValue<string>().Should().Be("string");
        field["nullable"].NotNull().GetValue<bool>().Should().BeFalse();
        // [MinLength(1)] became a non-empty constraint.
        var constraint = (JsonObject)field["constraints"].NotNull().AsArray().Single().NotNull();
        constraint["rule"].NotNull().GetValue<string>().Should().Be("nonEmpty");
    }

    [Test]
    public void Export_carries_guards_and_reducers_as_data()
    {
        var coin = Ir()["transitions"]
            .NotNull()
            .AsArray()
            .Cast<JsonObject>()
            .Single(t =>
                string.Equals(
                    t["trigger"].NotNull().GetValue<string>(),
                    "Coin",
                    StringComparison.Ordinal
                )
            );

        var guard = (JsonObject)coin["guard"].NotNull();
        guard["rule"].NotNull().GetValue<string>().Should().Be("oneOf");
        guard["source"].NotNull().GetValue<string>().Should().Be("input");
        guard["field"].NotNull().GetValue<string>().Should().Be("coin");
        guard["values"]
            .NotNull()
            .AsArray()
            .Select(v => v.NotNull().GetValue<string>())
            .Should()
            .Equal("quarter", "dollar");
        coin["guardMessage"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("Only a quarter or a dollar is accepted.");

        var reduce = (JsonObject)coin["reduce"].NotNull();
        reduce["reduce"].NotNull().GetValue<string>().Should().Be("set");
        var step = (JsonObject)reduce["steps"].NotNull().AsArray().Single().NotNull();
        step["field"].NotNull().GetValue<string>().Should().Be("paidWith");
        ((JsonObject)step["value"].NotNull())["input"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("coin");
    }

    [Test]
    public void Export_matches_the_committed_ir_golden()
    {
        // The full byte-exact IR for the declarative turnstile: identity, structure, per-state context
        // schema, and per-transition guard/reducer as data, canonical (keys sorted). A change to the
        // authoring or the exporter must move this deliberately.
        const string golden =
            """{"committedStates":[],"context":{"Locked":{"fields":[]},"Unlocked":{"fields":[{"constraints":[{"field":"paidWith","rule":"nonEmpty","source":"context"}],"name":"paidWith","nullable":false,"type":"string"}]}},"differential":{"samples":{"Coin":[{"coin":"quarter"},{"coin":"dollar"},{"coin":"penny"},{}]}},"id":"turnstile","initialContext":{},"initialState":"Locked","inputs":{"Coin":{"fields":[{"constraints":[],"name":"coin","nullable":false,"type":"string"}]}},"states":["Locked","Unlocked"],"transitions":[{"from":"Locked","guard":{"field":"coin","rule":"oneOf","source":"input","values":["quarter","dollar"]},"guardMessage":"Only a quarter or a dollar is accepted.","reduce":{"reduce":"set","steps":[{"field":"paidWith","value":{"input":"coin"}}]},"to":"Unlocked","trigger":"Coin"},{"from":"Unlocked","reduce":{"reduce":"clear"},"to":"Locked","trigger":"Push"}],"triggers":["Coin","Push"],"version":1}""";

        IrExporter.Export(DeclarativeTurnstile.Built).Should().Be(golden);
    }

    [Test]
    public void Export_carries_the_differential_block()
    {
        // The .Differential(...) samples authored on the turnstile become the IR's differential block, so the
        // cross-language harness enumerates off this IR instead of a hand-written machine.json. The empty {}
        // sample is preserved (distinct from the harness's always-added no-input case).
        var samples = (JsonObject)Ir()["differential"].NotNull()["samples"].NotNull();
        var coin = samples["Coin"].NotNull().AsArray();

        coin.Should().HaveCount(4);
        ((JsonObject)coin[0].NotNull())["coin"].NotNull().GetValue<string>().Should().Be("quarter");
        ((JsonObject)coin[1].NotNull())["coin"].NotNull().GetValue<string>().Should().Be("dollar");
        ((JsonObject)coin[2].NotNull())["coin"].NotNull().GetValue<string>().Should().Be("penny");
        coin[3].NotNull().AsObject().Count.Should().Be(0);
    }

    [Test]
    public void Export_omits_differential_when_none()
    {
        // A declarative machine that declares no .Differential(...) has no differential key, so adding the
        // feature leaves every existing machine's IR byte-identical.
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(RichExportMachine.Built)).NotNull();
        ir.ContainsKey("differential").Should().BeFalse();
    }

    [Test]
    public void Export_carries_the_trigger_input_schema()
    {
        var coin = (JsonObject)Ir()["inputs"].NotNull()["Coin"].NotNull();
        var field = (JsonObject)coin["fields"].NotNull().AsArray().Single().NotNull();

        field["name"].NotNull().GetValue<string>().Should().Be("coin");
        field["type"].NotNull().GetValue<string>().Should().Be("string");
    }

    // The IR reaches every browser, so an effect edge says only that it is effect-bound and which claim keys it
    // writes; the server's effect type stays on the server.
    [Test]
    public void Effect_edge_names_no_server_type()
    {
        var text = IrExporter.Export(RichExportMachine.Built);
        var ir = (JsonObject)JsonNode.Parse(text).NotNull();

        var cst = ir["transitions"]
            .NotNull()
            .AsArray()
            .Cast<JsonObject>()
            .Single(t =>
                string.Equals(
                    t["trigger"].NotNull().GetValue<string>(),
                    "Cst",
                    StringComparison.Ordinal
                )
            );
        var effect = (JsonObject)cst["effect"].NotNull();

        effect.Select(p => p.Key).Should().Equal("keyPrefix");
        effect["keyPrefix"].NotNull().GetValue<string>().Should().Be("rich:Cst");
        text.Should().NotContain("ThingEffect");
    }

    [Test]
    public void Export_of_a_rich_machine_covers_every_rule_and_reducer_kind()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(RichExportMachine.Built)).NotNull();

        ir["id"].NotNull().GetValue<string>().Should().Be("rich");
        ir["committedStates"]
            .NotNull()
            .AsArray()
            .Select(n => n.NotNull().GetValue<string>())
            .Should()
            .Contain("B");

        var transitions = ir["transitions"].NotNull().AsArray().Cast<JsonObject>().ToList();

        AssertCustomEffectEdge(transitions);

        // The Set1 guard is an `all` containing every simple rule kind.
        var set1 = transitions.Single(t =>
            string.Equals(
                t["trigger"].NotNull().GetValue<string>(),
                "Set1",
                StringComparison.Ordinal
            )
        );
        var kinds = ((JsonObject)set1["guard"].NotNull())["rules"]
            .NotNull()
            .AsArray()
            .Select(r => ((JsonObject)r.NotNull())["rule"].NotNull().GetValue<string>())
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
            .Select(t => ((JsonObject)t["reduce"].NotNull())["reduce"].NotNull().GetValue<string>())
            .ToList();
        reduceKinds.Should().Contain(new[] { "set", "clear", "reset", "keep", "custom" });
    }

    private static void AssertCustomEffectEdge(List<JsonObject> transitions)
    {
        // The Cst transition carries an effect (key derived from machine id + trigger), a custom guard, and
        // a custom reducer.
        var cst = transitions.Single(t =>
            string.Equals(
                t["trigger"].NotNull().GetValue<string>(),
                "Cst",
                StringComparison.Ordinal
            )
        );
        ((JsonObject)cst["effect"].NotNull())["keyPrefix"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("rich:Cst");
        ((JsonObject)cst["guard"].NotNull())["rule"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("custom");
        ((JsonObject)cst["reduce"].NotNull())["reduce"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("custom");
    }

    // A machine whose every edge and state is declarative except where a test adds a delegate.
    private static MachineBuilder<TurnstileState, TurnstileTrigger> MixedBuilder()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("mixed").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.CustomGuard("open", (_, _) => true);
        return m;
    }

    [Test]
    public void A_machine_mixing_a_delegate_guard_with_a_declarative_one_is_refused_export()
    {
        var m = MixedBuilder();
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .When((_, _) => false)
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked)
            .Context()
            .On(TurnstileTrigger.Push)
            .When(new Rule.Custom("open"))
            .To(TurnstileState.Locked);
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Locked -Coin-> Unlocked has a delegate guard*Rule.Custom*")
            .Which.Message.Should()
            .NotContain("Unlocked -Push-> Locked");
    }

    [Test]
    public void A_delegate_guard_set_after_a_rule_on_the_same_edge_is_refused_export()
    {
        var m = MixedBuilder();
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .When(new Rule.Custom("open"))
            .When((_, _) => false)
            .To(TurnstileState.Unlocked);
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Locked -Coin-> Unlocked has a delegate guard*");
    }

    [Test]
    public void A_machine_with_a_delegate_reducer_on_an_otherwise_declarative_machine_is_refused_export()
    {
        var m = MixedBuilder();
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .When(new Rule.Custom("open"))
            .Reduce((context, _) => context)
            .To(TurnstileState.Unlocked);
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Locked -Coin-> Unlocked has a delegate reducer*Reduction.Custom*");
    }

    [Test]
    public void A_machine_with_a_delegate_Holds_on_an_otherwise_declarative_machine_is_refused_export()
    {
        var m = MixedBuilder();
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .When(new Rule.Custom("open"))
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked).Holds(_ => null);
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*state Unlocked has a delegate validator (Holds)*Rule.Custom*")
            .Which.Message.Should()
            .NotContain("state Locked");
    }

    [Test]
    public void A_delegate_Holds_that_replaces_a_declared_context_is_refused_export()
    {
        var m = MixedBuilder();
        m.In(TurnstileState.Locked)
            .Context()
            .Holds(_ => null)
            .On(TurnstileTrigger.Coin)
            .When(new Rule.Custom("open"))
            .To(TurnstileState.Unlocked);
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*state Locked has a delegate validator (Holds)*");
    }

    private const string TimersAdr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public void Timers_export_as_their_own_trigger_kind()
    {
        var reminder = (JsonObject)
            JsonNode.Parse(IrExporter.Export(ReminderMachine.Built)).NotNull();
        var wait = (JsonObject)JsonNode.Parse(IrExporter.Export(WaitMachine.Built)).NotNull();

        // The fixed form, with the union of the rules entering the timed state.
        var fixedTimer = (JsonObject)
            reminder["timers"]
                .Should()
                .BeOfType<JsonArray>($"a timer is a trigger kind of its own ({TimersAdr})")
                .Subject.Should()
                .ContainSingle()
                .Subject.NotNull();
        fixedTimer["state"].NotNull().GetValue<string>().Should().Be("Review");
        fixedTimer["trigger"].NotNull().GetValue<string>().Should().Be("Review.after");
        fixedTimer["target"].NotNull().GetValue<string>().Should().Be("Expired");
        fixedTimer["after"].NotNull().GetValue<string>().Should().Be("PT10M");
        fixedTimer.ToJsonString().Should().Contain("Editor");

        // The field form, with its bounds.
        var fieldTimer = (JsonObject)wait["timers"].NotNull().AsArray().Single().NotNull();
        fieldTimer["trigger"].NotNull().GetValue<string>().Should().Be("Fetching.after");
        fieldTimer["target"].NotNull().GetValue<string>().Should().Be("TimedOut");
        fieldTimer["field"].NotNull()["name"].NotNull().GetValue<string>().Should().Be("patience");
        fieldTimer["field"].NotNull()["min"].NotNull().GetValue<string>().Should().Be("PT1M");
        fieldTimer["field"].NotNull()["max"].NotNull().GetValue<string>().Should().Be("PT1H");

        // Canonical: keys sorted, like every other block.
        foreach (var timer in new[] { fixedTimer, fieldTimer })
            timer.Select(kv => kv.Key).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Test]
    public void Timers_are_not_in_inputs()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(ReminderMachine.Built)).NotNull();

        ir["timers"]
            .Should()
            .BeOfType<JsonArray>("the timer is exported in a block of its own")
            .Which.Should()
            .ContainSingle();
        ((JsonObject)ir["inputs"].NotNull())
            .ContainsKey("Review.after")
            .Should()
            .BeFalse(TimersAdr);
        ir["triggers"]
            .NotNull()
            .AsArray()
            .Select(t => t.NotNull().GetValue<string>())
            .Should()
            .NotContain("Review.after", "the triggers list names what a client sends");
    }

    // Green today, and it stays so: adding timers leaves every existing golden byte-identical.
    [Test]
    public void A_machine_without_timers_exports_no_timers_block()
    {
        Ir().ContainsKey("timers").Should().BeFalse(TimersAdr);
        ((JsonObject)JsonNode.Parse(IrExporter.Export(IngestMachine.Built)).NotNull())
            .ContainsKey("timers")
            .Should()
            .BeFalse();
    }
}
