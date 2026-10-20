using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The canonical wire must match RFC 8785 (JCS) byte-for-byte, which for numbers and strings means matching
/// ECMAScript <c>JSON.stringify</c> exactly, since that is what the TypeScript twin emits. The vectors are
/// <c>conformance/canonical-wire.json</c>, which every engine reads; a divergence is a cross-runtime parity break,
/// the precise failure the differential exists to catch.
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

    public static IEnumerable<TestCaseData> Vectors()
    {
        var file = Path.Combine(FixturePaths.ConformanceRoot, "canonical-wire.json");
        foreach (var node in JsonNode.Parse(File.ReadAllText(file))!["cases"]!.AsArray())
        {
            var vector = (JsonObject)node!;
            yield return new TestCaseData(vector).SetArgDisplayNames(
                vector["name"]!.GetValue<string>()
            );
        }
    }

    [Test]
    public void The_shared_vectors_are_there() => Vectors().Should().HaveCountGreaterThan(20);

    [TestCaseSource(nameof(Vectors))]
    public void Serialize_writes_the_shared_vector(JsonObject vector)
    {
        Wire(ContextOf(vector)).Should().Be(Envelope(vector["wire"]!.GetValue<string>()));
    }

    // A lone surrogate has no UTF-8 encoding and System.Text.Json refuses to read one, so the file gives those
    // strings as UTF-16 code units.
    private static JsonObject ContextOf(JsonObject vector)
    {
        if (vector["context"] is { } context)
            return (JsonObject)JsonNode.Parse(context.GetValue<string>())!;
        if (vector["stringUnits"] is JsonArray stringUnits)
            return new JsonObject { ["s"] = FromUnits(stringUnits) };
        if (vector["keyUnits"] is JsonArray keyUnits)
            return new JsonObject { [FromUnits(keyUnits)] = 1 };
        throw new InvalidOperationException($"{vector["name"]}: no context");
    }

    private static string FromUnits(JsonArray units) =>
        new(units.Select(u => (char)u!.GetValue<int>()).ToArray());
}
