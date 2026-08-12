using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The canonical wire must match RFC 8785 (JCS) byte-for-byte, which for numbers and strings means matching
/// ECMAScript <c>JSON.stringify</c> exactly, since that is what the TypeScript twin emits. The expected
/// values here are the ECMAScript reference outputs (produced by <c>JSON.stringify</c>); a divergence is a
/// cross-runtime parity break, the precise failure the differential exists to catch.
/// </summary>
public class CanonicalizationConformanceTests
{
    private static string Wire(JsonObject context) =>
        TestTurnstile.Machine.Serialize(
            new Snapshot
            {
                Machine = "m",
                Version = 1,
                State = "S",
                Context = context,
            }
        );

    private static string Envelope(string context) =>
        "{\"machine\":\"m\",\"version\":1,\"state\":\"S\",\"context\":" + context + "}";

    [TestCase(0.1, "0.1")]
    [TestCase(1e21, "1e+21")]
    [TestCase(1e20, "100000000000000000000")]
    [TestCase(1e-7, "1e-7")]
    [TestCase(1e-6, "0.000001")]
    [TestCase(5e-324, "5e-324")]
    [TestCase(1.7976931348623157e308, "1.7976931348623157e+308")]
    [TestCase(1.2345678901234568e20, "123456789012345680000")]
    [TestCase(100.0, "100")]
    [TestCase(-1.5, "-1.5")]
    [TestCase(2.5, "2.5")]
    [TestCase(1.0, "1")]
    [TestCase(-0.0, "0")]
    public void Serialize_formats_numbers_per_ECMAScript(double value, string expected)
    {
        Wire(new JsonObject { ["n"] = value }).Should().Be(Envelope("{\"n\":" + expected + "}"));
    }

    [TestCase("café", "café")]
    [TestCase("a/b", "a/b")]
    [TestCase("q\"x", "q\\\"x")]
    [TestCase("b\\s", "b\\\\s")]
    public void Serialize_escapes_strings_like_JSON_stringify(string value, string expectedInner)
    {
        Wire(new JsonObject { ["s"] = value })
            .Should()
            .Be(Envelope("{\"s\":\"" + expectedInner + "\"}"));
    }

    [Test]
    public void Serialize_uses_short_escapes_and_lowercase_hex_for_controls()
    {
        // Input is U+0001, tab, newline, U+001F. Expected: short escapes where they exist, else lowercase
        // hex. The backslash is built from its code point so the literal escape text is not in the source.
        var input = new string([(char)0x01, (char)0x09, (char)0x0A, (char)0x1F]);
        var bs = (char)0x5C;
        var expectedInner = $"{bs}u0001{bs}t{bs}n{bs}u001f";
        Wire(new JsonObject { ["s"] = input })
            .Should()
            .Be(Envelope("{\"s\":\"" + expectedInner + "\"}"));
    }

    [Test]
    public void Serialize_uses_the_backspace_formfeed_and_carriage_return_short_escapes()
    {
        // U+0008, U+000C, U+000D have JSON.stringify short escapes \b \f \r (the control escapes the test
        // above does not exercise). Backslash built from its code point so the escape text is not in source.
        var input = new string([(char)0x08, (char)0x0C, (char)0x0D]);
        var bs = (char)0x5C;
        var expectedInner = $"{bs}b{bs}f{bs}r";
        Wire(new JsonObject { ["s"] = input })
            .Should()
            .Be(Envelope("{\"s\":\"" + expectedInner + "\"}"));
    }

    [Test]
    public void Serialize_keeps_astral_characters_literal()
    {
        Wire(new JsonObject { ["s"] = "😀" }).Should().Be(Envelope("{\"s\":\"😀\"}"));
    }
}
