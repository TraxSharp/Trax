using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A snapshot the engine accepts must have a canonical wire and must be storable, so <c>Rehydrate</c> refuses
/// the two valid-JSON values that have neither: a number outside double range and a NUL character.
/// </summary>
public class StorableJsonTests
{
    [TestCase("{\"n\":1e400}")]
    [TestCase("{\"n\":-1e400}")]
    [TestCase("{\"deep\":[{\"n\":1e309}]}")]
    [TestCase("{\"s\":\"a\\u0000b\"}")]
    [TestCase("{\"a\\u0000\":1}")]
    [TestCase("{\"list\":[\"ok\",\"\\u0000\"]}")]
    public void Finds_a_value_that_cannot_be_stored(string json)
    {
        StorableJson.Problem(JsonNode.Parse(json)).Should().NotBeNull();
    }

    [TestCase("{}")]
    [TestCase("{\"n\":1.7976931348623157e308,\"m\":5e-324,\"z\":-0}")]
    [TestCase("{\"s\":\"café 😀\",\"t\":true,\"u\":null,\"a\":[1,[2]]}")]
    public void Accepts_every_other_value(string json)
    {
        StorableJson.Problem(JsonNode.Parse(json)).Should().BeNull();
    }

    [Test]
    public void Finds_a_non_finite_double_built_in_code()
    {
        StorableJson.Problem(new JsonObject { ["n"] = double.NaN }).Should().NotBeNull();
        StorableJson
            .Problem(new JsonObject { ["n"] = double.PositiveInfinity })
            .Should()
            .NotBeNull();
        StorableJson.Problem(new JsonObject { ["n"] = 1.5m }).Should().BeNull();
    }

    [TestCase("{\"paidWith\":\"a\\u0000b\"}")]
    [TestCase("{\"paidWith\":\"q\",\"n\":1e400}")]
    public void Rehydrate_refuses_a_context_that_cannot_be_stored_as_malformed(string context)
    {
        var result = TestTurnstile.Machine.Rehydrate(
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":"
                + context
                + "}"
        );

        result
            .Should()
            .BeOfType<RehydrationResult.Error>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.Malformed);
    }
}
