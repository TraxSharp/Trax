using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The engine checks a trigger's input against the schema its <c>WithInput</c> record declares before any guard
/// runs, as strictly as the CLR type, and refuses a mismatch as <c>invalid-input</c> without throwing.
/// </summary>
[Property("adr", Adr)]
public class TriggerInputValidationTests
{
    internal const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static AdvanceResult Build(JsonNode? input) =>
        RangeMachine.Machine.Advance(RangeMachine.Open(), nameof(RangeTrigger.Build), input);

    private static void ShouldBeInvalidInput(AdvanceResult result) =>
        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"an input that does not match its trigger's schema is refused before any guard ({Adr})"
            );

    private static JsonObject ValidWith(Action<JsonObject> change)
    {
        var input = RangeMachine.ValidInput();
        change(input);
        return input;
    }

    [Test]
    public void The_valid_input_transitions()
    {
        Build(RangeMachine.ValidInput()).Should().BeOfType<AdvanceResult.Transitioned>();
    }

    [Test]
    public void A_missing_required_input_field_is_refused_as_invalid_input()
    {
        ShouldBeInvalidInput(Build(ValidWith(i => i.Remove("from"))));
    }

    [Test]
    public void A_number_sent_as_a_string_is_refused_as_invalid_input()
    {
        ShouldBeInvalidInput(Build(ValidWith(i => i["from"] = "2020")));
    }

    [Test]
    public void An_unknown_input_field_is_refused_as_invalid_input()
    {
        ShouldBeInvalidInput(Build(ValidWith(i => i["x"] = 1)));
    }

    [Test]
    public void A_field_named_in_PascalCase_is_an_unknown_field_not_an_alias()
    {
        var input = new JsonObject
        {
            ["From"] = 1,
            ["to"] = 2,
            ["note"] = null,
            ["tags"] = new JsonArray("a"),
        };

        ShouldBeInvalidInput(Build(input));
    }

    [TestCase("\"s\"")]
    [TestCase("1")]
    [TestCase("[]")]
    [TestCase("true")]
    [TestCase("null")]
    public void A_non_object_input_is_refused_as_invalid_input(string json)
    {
        ShouldBeInvalidInput(Build(JsonNode.Parse(json)));
    }

    [Test]
    public void An_input_constraint_is_checked_against_the_input_not_the_context()
    {
        // The context's tags are non-empty, so a [MinLength] read from the context would pass.
        RangeMachine.Open().Context["tags"]!
            .AsArray()
            .Should()
            .NotBeEmpty();

        ShouldBeInvalidInput(Build(ValidWith(i => i["tags"] = new JsonArray())));
    }

    [Test]
    public void A_trigger_without_WithInput_refuses_unknown_fields()
    {
        var open = RangeMachine.Open();
        var poke = nameof(RangeTrigger.Poke);

        ShouldBeInvalidInput(
            RangeMachine.Machine.Advance(open, poke, new JsonObject { ["x"] = 1 })
        );
        RangeMachine
            .Machine.Advance(open, poke, null)
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();
        RangeMachine
            .Machine.Advance(open, poke, new JsonObject())
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();
    }

    [Test]
    public void An_int_field_refuses_a_fraction_and_accepts_an_integral_value()
    {
        ShouldBeInvalidInput(Build(ValidWith(i => i["from"] = 1.5)));
        // JavaScript cannot tell 2020.0 from 2020, so the verdict is decided by the value.
        Build(JsonNode.Parse("""{"from":2020.0,"to":2,"note":null,"tags":["a"]}"""))
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();
    }

    [Test]
    public void A_number_beyond_double_range_gets_one_verdict_in_both_engines()
    {
        // C# keeps the text 1e400; JavaScript reads Infinity and writes null. Both refuse it.
        ShouldBeInvalidInput(
            Build(JsonNode.Parse("""{"from":1e400,"to":2,"note":null,"tags":["a"]}"""))
        );
    }

    [Test]
    public void A_lone_surrogate_in_an_input_string_is_refused_not_an_internal_error()
    {
        ShouldBeInvalidInput(Build(ValidWith(i => i["note"] = "\uD800")));
    }

    [Test]
    public void A_duplicate_key_in_the_input_is_refused_not_thrown()
    {
        // A JsonObject parsed with a duplicate key throws when it is enumerated, so validation has to sit
        // inside the engine's totality backstop.
        var input = JsonNode.Parse("""{"from":1,"from":2,"to":2,"note":null,"tags":["a"]}""");

        AdvanceResult result = null!;
        var advance = () => result = Build(input);

        advance.Should().NotThrow();
        ShouldBeInvalidInput(result);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Two_edges_declaring_different_input_types_for_one_trigger_refuse_to_build(
        bool secondDeclaresAnInput
    )
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("range").StartsAt(RangeState.Open, () => new JsonObject());
        m.In(RangeState.Open)
            .Context()
            .On(RangeTrigger.Build)
            .WithInput<RangeMachine.RangeInput>()
            .To(RangeState.Ranged);
        var second = m.In(RangeState.Ranged).Context().On(RangeTrigger.Build);
        if (secondDeclaresAnInput)
            second = second.WithInput<OtherInput>();
        second.To(RangeState.Open);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"one trigger has one input schema, which every engine validates against ({Adr})"
            )
            .WithMessage("*Build*");
    }

    [Test]
    public void A_JsonPropertyName_on_an_input_record_is_honoured()
    {
        var engine = NamedMachine();
        var open = new Snapshot
        {
            Machine = "named",
            Version = 1,
            State = nameof(RangeState.Open),
            Context = new JsonObject(),
        };
        var build = nameof(RangeTrigger.Build);

        engine
            .Engine.Advance(open, build, new JsonObject { ["from_year"] = 1 })
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();
        ShouldBeInvalidInput(
            engine.Engine.Advance(open, build, new JsonObject { ["fromYear"] = 1 })
        );

        var fields = ((JsonObject)JsonNode.Parse(IrExporter.Export(engine))!)["inputs"]!["Build"]![
            "fields"
        ]!
            .AsArray()
            .Select(f => f!["name"]!.GetValue<string>());
        fields.Should().Equal(["from_year"], "a [JsonIgnore] member is not part of the input");
    }

    [Test]
    public void A_recursive_input_type_refuses_to_build()
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("recursive").StartsAt(RangeState.Open, () => new JsonObject());

        var build = () =>
        {
            m.In(RangeState.Open)
                .Context()
                .On(RangeTrigger.Build)
                .WithInput<Node>()
                .To(RangeState.Ranged);
            m.In(RangeState.Ranged).Context();
            return m.Build();
        };

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"a recursive input has no finite schema to validate against ({Adr})"
            )
            .WithMessage("*Node*");
    }

    [Test]
    public void A_nested_record_input_is_validated_as_strictly_as_its_CLR_type()
    {
        var engine = NestedMachine();
        var open = new Snapshot
        {
            Machine = "nested",
            Version = 1,
            State = nameof(RangeState.Open),
            Context = new JsonObject(),
        };
        var build = nameof(RangeTrigger.Build);
        JsonObject Valid() =>
            new()
            {
                ["inner"] = new JsonObject { ["count"] = 1 },
                ["items"] = new JsonArray(new JsonObject { ["count"] = 2 }),
            };

        engine.Advance(open, build, Valid()).Should().BeOfType<AdvanceResult.Transitioned>();

        var wrongLeaf = Valid();
        wrongLeaf["inner"]!["count"] = "one";
        ShouldBeInvalidInput(engine.Advance(open, build, wrongLeaf));

        var wrongElement = Valid();
        wrongElement["items"] = new JsonArray(new JsonObject { ["count"] = "two" });
        ShouldBeInvalidInput(engine.Advance(open, build, wrongElement));
    }

    [Test]
    public void A_deeply_nested_input_gets_a_verdict_without_overflowing()
    {
        var engine = NestedMachine();
        var open = new Snapshot
        {
            Machine = "nested",
            Version = 1,
            State = nameof(RangeState.Open),
            Context = new JsonObject(),
        };

        // A thousand levels of inner objects, built directly so no parser depth limit refuses it first.
        var input = new JsonObject { ["count"] = 1 };
        for (var depth = 0; depth < 1000; depth++)
            input = new JsonObject { ["inner"] = input, ["count"] = depth };
        var items = new JsonArray();
        input = new JsonObject { ["inner"] = input, ["items"] = items };

        var advance = Task.Run(() => engine.Advance(open, nameof(RangeTrigger.Build), input));

        // The test owns its wait (ADR 0014): a verdict well within it, or the test fails.
        advance.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        ShouldBeInvalidInput(advance.Result);
    }

    // A record whose wire name is set by [JsonPropertyName], and a member left out by [JsonIgnore].
    public sealed record NamedInput
    {
        [JsonPropertyName("from_year")]
        public int FromYear { get; init; }

        [JsonIgnore]
        public string Internal { get; init; } = "";
    }

    public sealed record OtherInput
    {
        public string Label { get; init; } = "";
    }

    public sealed record Node
    {
        public Node? Next { get; init; }
    }

    public sealed record Inner
    {
        public int Count { get; init; }
    }

    public sealed record NestedInput
    {
        public Inner Inner { get; init; } = new();
        public List<Inner> Items { get; init; } = [];
    }

    private static BuiltMachine<RangeState, RangeTrigger> NamedMachine()
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("named").StartsAt(RangeState.Open, () => new JsonObject());
        m.In(RangeState.Open)
            .Context()
            .On(RangeTrigger.Build)
            .WithInput<NamedInput>()
            .To(RangeState.Ranged);
        m.In(RangeState.Ranged).Context();
        return m.Build();
    }

    private static SnapshotMachine<RangeState, RangeTrigger> NestedMachine()
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("nested").StartsAt(RangeState.Open, () => new JsonObject());
        m.In(RangeState.Open)
            .Context()
            .On(RangeTrigger.Build)
            .WithInput<NestedInput>()
            .To(RangeState.Ranged);
        m.In(RangeState.Ranged).Context();
        return m.Build().Engine;
    }
}
