using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Targets the less-trodden branches of <see cref="TypedQueryGenerator"/>:
/// <list type="bullet">
///   <item><c>[GraphQLField]</c> overrides the property->field name mapping.</item>
///   <item><c>[JsonIgnore]</c> properties are skipped entirely.</item>
///   <item>Nested objects produce nested selection sets.</item>
///   <item>Operation Name override on the attribute is honored.</item>
/// </list>
/// </summary>
[GraphQLType("Player")]
public sealed record FieldRenamedPlayer(
    string Id,
    // Two attributes: [GraphQLField] drives the GENERATED query's selection set;
    // [JsonPropertyName] drives DESERIALIZATION of the response. Apply both when the
    // CLR property name differs from the schema field name. The generator and the JSON
    // deserializer are separate concerns - one tells the server what to send, the other
    // tells STJ how to bind it back.
    [property: GraphQLField("name"), JsonPropertyName("name")] string DisplayName,
    [property: JsonIgnore] string? InternalNote,
    int? Level
);

[GraphQLOperation(OperationType.Query, Name = "GetPlayerCustomOp", RootField = "player")]
public sealed class FieldRenamedRequest : TypedRequest<FieldRenamedPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
