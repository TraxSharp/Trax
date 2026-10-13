using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The exported schema is as strict as the CLR type: an integer field carries its CLR type's range, the values of
/// a dictionary and the elements of a list are checked against their own type, and a field is named exactly as
/// the server's camelCase serializer names it. The TypeScript twin's <c>schemaStrictness.test.ts</c> checks the
/// same inputs against the same exported numbers.
/// </summary>
[Property("adr", Adr)]
public class SchemaStrictnessTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    public sealed record Widths
    {
        public int Int { get; init; }
        public long Long { get; init; }
        public short Short { get; init; }
        public byte Byte { get; init; }
        public sbyte SByte { get; init; }
        public uint UInt { get; init; }
        public ulong ULong { get; init; }
        public ushort UShort { get; init; }
        public int? MaybeInt { get; init; }
    }

    public sealed record Inner
    {
        public int Count { get; init; }
    }

    public sealed record Maps
    {
        public Dictionary<string, int> Counts { get; init; } = [];
        public IReadOnlyDictionary<string, string?> Notes { get; init; } =
            new Dictionary<string, string?>();
        public Dictionary<string, Inner> Inners { get; init; } = [];
        public Dictionary<string, List<byte>> Bytes { get; init; } = [];
        public List<long> Longs { get; init; } = [];
        public Dictionary<string, object> Anything { get; init; } = [];
    }

    public sealed record Acronyms
    {
        public string ID { get; init; } = "";
        public string URLValue { get; init; } = "";
        public string PaidWith { get; init; } = "";
    }

    private static FieldSchema Field(ContextSchema schema, string name) =>
        schema.Fields.Single(f => f.Name == name);

    private static string? Validate<T>(JsonObject input) =>
        SchemaValidator.ValidateInput(SchemaReflection.ForInput(typeof(T)), input);

    // A valid Widths input with one field replaced by the given JSON number text.
    private static JsonObject WidthsWith(string field, string number)
    {
        var input = new JsonObject
        {
            ["int"] = 0,
            ["long"] = 0,
            ["short"] = 0,
            ["byte"] = 0,
            ["sByte"] = 0,
            ["uInt"] = 0,
            ["uLong"] = 0,
            ["uShort"] = 0,
            ["maybeInt"] = null,
        };
        input[field] = JsonNode.Parse(number);
        return input;
    }

    #region Integer ranges

    [TestCase("int", -2147483648d, 2147483647d)]
    [TestCase("short", -32768d, 32767d)]
    [TestCase("byte", 0d, 255d)]
    [TestCase("sByte", -128d, 127d)]
    [TestCase("uInt", 0d, 4294967295d)]
    [TestCase("uShort", 0d, 65535d)]
    [TestCase("maybeInt", -2147483648d, 2147483647d)]
    // Past 2^53 the bound is the nearest double inside the range, since a JSON number reaches both engines as a
    // double and long.MaxValue reads as 2^63, which is past it.
    [TestCase("long", -9223372036854774784d, 9223372036854774784d)]
    [TestCase("uLong", 0d, 18446744073709549568d)]
    public void An_integer_field_exports_its_CLR_range(string name, double min, double max)
    {
        var field = Field(SchemaReflection.ForInput(typeof(Widths)), name);

        field.Minimum.Should().Be(min, $"an integer field is as strict as its CLR type ({Adr})");
        field.Maximum.Should().Be(max);
    }

    [Test]
    public void The_range_is_written_to_the_IR()
    {
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("widths").StartsAt(RangeState.Open, () => new JsonObject());
        m.In(RangeState.Open)
            .Context()
            .On(RangeTrigger.Build)
            .WithInput<Widths>()
            .To(RangeState.Ranged);
        m.In(RangeState.Ranged).Context();
        var ir = JsonNode.Parse(IrExporter.Export(m.Build()))!;

        var fields = ir["inputs"]!["Build"]!["fields"]!.AsArray();
        var @long = fields.Single(f => f!["name"]!.GetValue<string>() == "long")!;
        @long["minimum"]!.GetValue<double>().Should().Be(-9223372036854774784d);
        @long["maximum"]!.GetValue<double>().Should().Be(9223372036854774784d);
        var @byte = fields.Single(f => f!["name"]!.GetValue<string>() == "byte")!;
        @byte["minimum"]!.GetValue<double>().Should().Be(0);
        @byte["maximum"]!.GetValue<double>().Should().Be(255);
    }

    [TestCase("int", "1e10")]
    [TestCase("int", "2147483648")]
    [TestCase("int", "-2147483649")]
    [TestCase("short", "32768")]
    [TestCase("byte", "256")]
    [TestCase("byte", "-1")]
    [TestCase("sByte", "128")]
    [TestCase("uInt", "-1")]
    [TestCase("uInt", "4294967296")]
    [TestCase("uShort", "65536")]
    [TestCase("maybeInt", "1e10")]
    // long.MaxValue + 1 and long.MaxValue are one double; neither engine can tell them apart, so both refuse.
    [TestCase("long", "9223372036854775808")]
    [TestCase("long", "9223372036854775807")]
    [TestCase("long", "-9223372036854775809")]
    [TestCase("long", "1e19")]
    [TestCase("uLong", "-1")]
    [TestCase("uLong", "18446744073709551616")]
    public void An_integer_outside_its_CLR_range_is_refused(string field, string number)
    {
        Validate<Widths>(WidthsWith(field, number))
            .Should()
            .NotBeNull($"{number} does not fit the CLR type of '{field}' ({Adr})");
    }

    [TestCase("int", "2147483647")]
    [TestCase("int", "-2147483648")]
    [TestCase("short", "-32768")]
    [TestCase("byte", "255")]
    [TestCase("sByte", "-128")]
    [TestCase("uInt", "4294967295")]
    [TestCase("uShort", "65535")]
    [TestCase("long", "9223372036854774784")]
    [TestCase("long", "-9223372036854774784")]
    [TestCase("long", "9007199254740993")]
    [TestCase("uLong", "18446744073709549568")]
    [TestCase("int", "1e9")]
    public void An_integer_inside_its_CLR_range_is_accepted(string field, string number)
    {
        Validate<Widths>(WidthsWith(field, number)).Should().BeNull();
    }

    [Test]
    public void An_out_of_range_input_is_refused_as_invalid_input_by_the_engine()
    {
        var input = RangeMachine.ValidInput();
        input["from"] = JsonNode.Parse("1e10");

        RangeMachine
            .Machine.Advance(RangeMachine.Open(), nameof(RangeTrigger.Build), input)
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"1e10 is an integer, but not an int: it is refused before any guard ({Adr})"
            );
    }

    #endregion

    #region Dictionary values and list elements

    private static JsonObject ValidMaps() =>
        new()
        {
            ["counts"] = new JsonObject { ["a"] = 1 },
            ["notes"] = new JsonObject { ["a"] = "x", ["b"] = null },
            ["inners"] = new JsonObject { ["a"] = new JsonObject { ["count"] = 1 } },
            ["bytes"] = new JsonObject { ["a"] = new JsonArray(1, 255) },
            ["longs"] = new JsonArray(1, 2),
            ["anything"] = new JsonObject { ["a"] = new JsonArray(), ["b"] = "x" },
        };

    private static JsonObject MapsWith(Action<JsonObject> change)
    {
        var input = ValidMaps();
        change(input);
        return input;
    }

    [Test]
    public void A_valid_map_input_is_accepted()
    {
        Validate<Maps>(ValidMaps()).Should().BeNull();
    }

    [Test]
    public void The_value_schema_of_a_dictionary_comes_from_its_value_type()
    {
        var schema = SchemaReflection.ForInput(typeof(Maps));

        var counts = Field(schema, "counts").Values!;
        counts.Type.Should().Be(JsonFieldType.Integer);
        counts.Nullable.Should().BeFalse();
        counts.Maximum.Should().Be(int.MaxValue);
        Field(schema, "notes").Values!.Nullable.Should().BeTrue("the value type is string?");
        Field(schema, "inners").Values!.Fields!.Single().Name.Should().Be("count");
        Field(schema, "bytes").Values!.Values!.Maximum.Should().Be(255);
        Field(schema, "longs").Values!.Type.Should().Be(JsonFieldType.Integer);
        Field(schema, "anything")
            .Values.Should()
            .BeNull("an object value has no fixed JSON type, like a JsonObject field's");
    }

    [Test]
    public void A_dictionary_value_of_the_wrong_type_is_refused()
    {
        Validate<Maps>(MapsWith(i => i["counts"]!["a"] = "one"))
            .Should()
            .NotBeNull($"a Dictionary<string, int> value must be an int ({Adr})");
    }

    [Test]
    public void A_dictionary_value_out_of_its_range_is_refused()
    {
        Validate<Maps>(MapsWith(i => i["counts"]!["a"] = JsonNode.Parse("1e10")))
            .Should()
            .NotBeNull();
    }

    [Test]
    public void A_null_dictionary_value_is_refused_unless_the_value_type_is_nullable()
    {
        Validate<Maps>(MapsWith(i => i["counts"]!["a"] = null)).Should().NotBeNull();
        Validate<Maps>(MapsWith(i => i["notes"]!["a"] = null)).Should().BeNull();
    }

    [Test]
    public void A_dictionary_of_records_checks_each_record()
    {
        Validate<Maps>(MapsWith(i => i["inners"]!["a"]!["count"] = "one")).Should().NotBeNull();
        Validate<Maps>(MapsWith(i => i["inners"]!["a"]!["extra"] = 1))
            .Should()
            .NotBeNull("a record value refuses a field its type does not declare");
    }

    [Test]
    public void A_dictionary_of_lists_checks_each_element()
    {
        Validate<Maps>(MapsWith(i => i["bytes"]!["a"] = new JsonArray(1, 256)))
            .Should()
            .NotBeNull();
    }

    [Test]
    public void A_list_element_out_of_its_range_is_refused()
    {
        Validate<Maps>(MapsWith(i => i["longs"] = new JsonArray(1, JsonNode.Parse("1e19"))))
            .Should()
            .NotBeNull();
    }

    [Test]
    public void A_dictionary_value_check_runs_in_a_context_too()
    {
        var schema = SchemaReflection.For<Maps>();

        SchemaValidator
            .Validate(schema, MapsWith(i => i["counts"]!["a"] = "one"))
            .Should()
            .NotBeNull("a context's dictionary values are typed like an input's");
    }

    #endregion

    #region Field names

    [Test]
    public void A_field_is_named_as_the_server_serializer_names_it()
    {
        var schema = SchemaReflection.ForInput(typeof(Acronyms));
        // The naming the server stores a train's output with and the engine serializes an outcome with.
        var serialized = System.Text.Json.JsonSerializer.SerializeToNode(
            new Acronyms(),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }
        )!;

        schema
            .Fields.Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                serialized.AsObject().Select(kv => kv.Key),
                $"the name a client sends is the name the server binds ({Adr})"
            );
        schema.Fields.Select(f => f.Name).Should().Equal("id", "paidWith", "urlValue");
    }

    #endregion
}
