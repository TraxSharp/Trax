using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A string field is as strict as its CLR type: an enum exports its names and refuses any other, a Guid, a date,
/// a time, a duration, a URI and a char export a format each value must have, and a decimal, a float and a Half
/// carry their CLR range. The cross-engine half is <c>StrictInputParityTests</c>, which sends the same cases
/// through the TypeScript twin.
/// </summary>
[Property("adr", Adr)]
public class SchemaFormatTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static FieldSchema Field(string name) =>
        SchemaReflection
            .ForInput(typeof(StrictInputMachine.StrictInput))
            .Fields.Single(f => f.Name == name);

    [Test]
    public void An_enum_exports_its_names_as_the_machine_json_writes_them()
    {
        Field("color").Enum.Should().Equal(["red", "darkGreen", "sky-blue"]);
        Field("maybeColor").Enum.Should().Equal(["red", "darkGreen", "sky-blue"]);
        Field("palette").Values!.Enum.Should().Equal(["red", "darkGreen", "sky-blue"]);
    }

    [Test]
    public void A_renamed_enum_member_is_compared_exactly()
    {
        Field("color")
            .EnumExact.Should()
            .Equal(
                ["sky-blue"],
                "System.Text.Json reads a [JsonStringEnumMemberName] only as written"
            );
        Field("palette").Values!.EnumExact.Should().Equal(["sky-blue"]);
        SchemaReflection
            .ForInput(typeof(PlainEnumInput))
            .Fields.Single()
            .EnumExact.Should()
            .BeNull("an enum that renames no member is compared ignoring ASCII case");
    }

    private sealed record PlainEnumInput(StrictState State);

    private sealed record FlagsInput(StrictFlags Style);

    private sealed record FlagsListInput(List<StrictFlags> Styles);

    private sealed record FlagsContext(StrictFlags Style);

    [Test]
    public void A_flags_enum_has_no_fixed_JSON_type_and_refuses_to_build()
    {
        // System.Text.Json writes a flags value as "Bold, Italic", or as the number 8 when no names cover it.
        foreach (
            var reflect in new Action[]
            {
                () => SchemaReflection.ForInput(typeof(FlagsInput)),
                () => SchemaReflection.ForInput(typeof(FlagsListInput)),
                () => SchemaReflection.For(typeof(FlagsContext)),
            }
        )
            reflect
                .Should()
                .Throw<InvalidOperationException>(
                    $"a schema accepts only what the CLR type reads back ({Adr})"
                )
                .WithMessage("*[Flags]*StrictFlags*");
    }

    [TestCase("id", "uuid")]
    [TestCase("at", "date-time")]
    [TestCase("atOffset", "date-time-offset")]
    [TestCase("day", "date")]
    [TestCase("clock", "time")]
    [TestCase("patience", "duration")]
    [TestCase("link", "uri")]
    [TestCase("initial", "char")]
    public void A_string_with_a_CLR_shape_exports_its_format(string name, string format) =>
        Field(name).Format.Should().Be(format, $"the schema is as strict as the CLR type ({Adr})");

    public sealed record WithConstructor(int Qty)
    {
        public int Qty { get; } = Qty;
        public int Twice => Qty * 2;

        [System.Text.Json.Serialization.JsonInclude]
        public int Hidden { get; private set; }

        public int Unread { get; private set; }
    }

    [Test]
    public void A_property_reading_never_sets_is_not_an_input_field()
    {
        SchemaReflection
            .ForInput(typeof(StrictInputMachine.StrictInput))
            .Fields.Select(f => f.Name)
            .Should()
            .Contain(["qty", "price"])
            .And.NotContain("total", "System.Text.Json skips a computed property on read");
        SchemaReflection
            .ForInput(typeof(WithConstructor))
            .Fields.Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                ["qty", "hidden"],
                "a constructor parameter and a [JsonInclude] setter are read; a computed property and a private setter are not"
            );
    }

    [Test]
    public void A_dictionary_s_keys_carry_their_CLR_key_type()
    {
        Field("owners").Keys.Should().BeNull("a string key is any string");
        Field("byNumber").Keys!.Type.Should().Be(JsonFieldType.Integer);
        Field("byNumber").Keys!.Maximum.Should().Be(int.MaxValue);
        Field("byColor").Keys!.Enum.Should().Equal(["red", "darkGreen", "sky-blue"]);
        Field("byColor").Keys!.EnumExact.Should().Equal(["sky-blue"]);
        Field("byId").Keys!.Format.Should().Be("uuid");
    }

    private sealed record DurationKeyed(Dictionary<TimeSpan, int> ByWait);

    private sealed record BoolKeyed(Dictionary<bool, int> ByFlag);

    [Test]
    public void A_dictionary_keyed_by_a_type_no_schema_checks_refuses_to_build()
    {
        foreach (var type in new[] { typeof(DurationKeyed), typeof(BoolKeyed) })
            FluentActions
                .Invoking(() => SchemaReflection.ForInput(type))
                .Should()
                .Throw<InvalidOperationException>(type.Name)
                .WithMessage("*dictionary keyed by*");
    }

    [Test]
    public void The_values_of_a_dictionary_of_guids_carry_the_format() =>
        Field("owners").Values!.Format.Should().Be("uuid");

    [TestCase("amount", -79228162514264337593543950335d, 79228162514264337593543950335d)]
    [TestCase("ratio", -3.4028235e38, 3.4028235e38)]
    [TestCase("small", -65504d, 65504d)]
    public void A_fractional_number_exports_its_CLR_range(string name, double min, double max)
    {
        var field = Field(name);
        field.Minimum.Should().NotBeNull().And.BeGreaterThanOrEqualTo(min);
        field.Maximum.Should().NotBeNull().And.BeLessThanOrEqualTo(max);
        field.Maximum!.Value.Should().BeApproximately(max, Math.Abs(max) * 1e-15);
    }

    [Test]
    public void A_double_has_no_range_beyond_finite()
    {
        Field("wide").Minimum.Should().BeNull();
        Field("wide").Maximum.Should().BeNull();
    }

    [Test]
    public void The_enum_format_and_range_are_written_to_the_IR()
    {
        var ir = JsonNode.Parse(IrExporter.Export(StrictInputMachine.Built))!;
        var fields = ir["inputs"]!["Submit"]!["fields"]!.AsArray();
        JsonNode F(string name) => fields.Single(f => f!["name"]!.GetValue<string>() == name)!;

        F("color")["enum"]!.ToJsonString().Should().Be("""["red","darkGreen","sky-blue"]""");
        F("color")["enumExact"]!.ToJsonString().Should().Be("""["sky-blue"]""");
        F("id")["format"]!.GetValue<string>().Should().Be("uuid");
        F("palette")["values"]!["enum"]!.ToJsonString().Should().Contain("sky-blue");
        F("owners")["values"]!["format"]!.GetValue<string>().Should().Be("uuid");
        F("byNumber")["keys"]!["type"]!.GetValue<string>().Should().Be("integer");
        F("byId")["keys"]!["format"]!.GetValue<string>().Should().Be("uuid");
        F("amount")["maximum"]!.GetValue<double>().Should().BeGreaterThan(7.9e28);
        F("wide")["maximum"].Should().BeNull();
    }

    private static readonly string[] Schemes =
    [
        "http",
        "HTTPS",
        "ws",
        "ftp",
        "file",
        "File",
        "mailto",
        "news",
        "uuid",
        "ldap",
        "net.tcp",
        "net.pipe",
        "urn",
        "ab",
        "git+ssh",
        "x-y.z",
    ];

    private static readonly Gen<string> UriText = Gen.String[
        Gen.Char["aZ09-._~!$&'()*+,;=:@/?#%[]Fb.:/@"],
        0,
        16
    ];

    private static readonly Gen<string> AHost = Gen.OneOf(
        UriText.Select(t => t.Replace("/", "").Replace("?", "").Replace("#", "")),
        Gen.Int[0, 300].Array[1, 5].Select(octets => string.Join(".", octets)),
        Gen.Int[0, 65535]
            .Array[1, 9]
            .Select(groups => "[" + string.Join(":", groups.Select(g => g.ToString("x"))) + "]"),
        Gen.Int[0, 65535].Select(g => $"[::{g:x}]"),
        UriText.Select(t => "[" + t + "]")
    );

    private static readonly Gen<string> AUriLikeString = Gen.Select(
        Gen.OneOfConst(Schemes),
        Gen.Int[0, 3],
        AHost,
        Gen.Int[-1, 70000],
        UriText,
        (scheme, shape, host, port, tail) =>
            shape switch
            {
                0 => $"{scheme}:{tail}",
                1 => $"{scheme}://{host}{tail}",
                2 => $"{scheme}://{host}:{port}{tail}",
                _ => $"{scheme}://{tail}@{host}{(port < 0 ? "" : ":" + port)}/{tail}",
            }
    );

    [Test]
    public void Every_uri_the_format_accepts_is_one_System_Text_Json_reads()
    {
        // The twin checks the same grammar without System.Uri to ask, so the grammar alone must be the stricter.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        AUriLikeString.Sample(
            uri =>
            {
                if (!ValueFormats.IsPortableUri(uri))
                    return true;
                try
                {
                    JsonSerializer.Deserialize<Uri>(JsonSerializer.Serialize(uri), options);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            },
            iter: 200_000
        );
    }

    [Test]
    public void The_valid_input_transitions() =>
        StrictInputMachine
            .Machine.Advance(
                StrictInputMachine.Open(),
                nameof(StrictTrigger.Submit),
                StrictInputMachine.ValidInput()
            )
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();

    private static IEnumerable<TestCaseData> Cases() =>
        StrictInputMachine.Cases.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Name));

    [TestCaseSource(nameof(Cases))]
    public void Each_value_gets_the_verdict_of_its_CLR_type(StrictInputMachine.Case c)
    {
        var result = StrictInputMachine.Machine.Advance(
            StrictInputMachine.Open(),
            nameof(StrictTrigger.Submit),
            StrictInputMachine.InputWith(c.Field, c.Value?.DeepClone())
        );

        if (c.Valid)
            result.Should().BeOfType<AdvanceResult.Transitioned>(c.Name);
        else
            result
                .Should()
                .BeOfType<AdvanceResult.Rejected>(c.Name)
                .Which.Reason.Should()
                .Be(
                    RejectionReasons.InvalidInput,
                    $"{c.Name} is not a value its CLR type holds ({Adr})"
                );
    }

    [Test]
    public void An_unknown_enum_name_is_refused_on_a_trigger_whose_guard_is_a_delegate()
    {
        var engine = StrictInputMachine.WithDelegateGuard.Engine;
        var open = StrictInputMachine.Open("strict-delegate");

        engine
            .Advance(open, nameof(StrictTrigger.Check), StrictInputMachine.ValidInput())
            .Should()
            .BeOfType<AdvanceResult.Transitioned>();
        engine
            .Advance(
                open,
                nameof(StrictTrigger.Check),
                StrictInputMachine.InputWith("color", "purple")
            )
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.InvalidInput,
                "WithInput names the record, so the delegate's trigger is validated against it"
            );
        var unknown = StrictInputMachine.ValidInput();
        unknown["extra"] = 1;
        engine
            .Advance(open, nameof(StrictTrigger.Check), unknown)
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.InvalidInput);
    }
}
