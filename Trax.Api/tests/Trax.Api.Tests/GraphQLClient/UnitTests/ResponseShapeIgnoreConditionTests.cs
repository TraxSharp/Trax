using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// Only <c>[JsonIgnore]</c> with <c>Condition = Always</c> keeps a property out of
/// deserialization. A <c>WhenWritingNull</c> or <c>WhenWritingDefault</c> property is still read
/// from the response, so strict mode expects it in the response like any other property.
/// </summary>
[TestFixture]
public class ResponseShapeIgnoreConditionTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Test]
    public void A_WhenWritingNull_property_missing_from_the_response_is_drift()
    {
        using var doc = JsonDocument.Parse("""{"id":"p1","name":"Aragorn"}""");

        var act = () =>
            ResponseShapeValidator.Validate(
                doc.RootElement,
                typeof(ConditionallyIgnoredShape),
                ResponseStrictness.ThrowOnDrift,
                Options,
                logger: null
            );

        act.Should()
            .Throw<GraphQLResponseShapeException>()
            .Which.MissingJsonFields.Should()
            .ContainSingle(f => f.Equals("level", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void An_always_ignored_property_is_not_expected_in_the_response()
    {
        using var doc = JsonDocument.Parse("""{"id":"p1","name":"Aragorn","level":3}""");

        var act = () =>
            ResponseShapeValidator.Validate(
                doc.RootElement,
                typeof(ConditionallyIgnoredShape),
                ResponseStrictness.ThrowOnDrift,
                Options,
                logger: null
            );

        act.Should().NotThrow();
    }

    private sealed record ConditionallyIgnoredShape(
        string Id,
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level,
        [property: JsonIgnore] string? LocalNote
    );
}
