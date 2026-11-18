using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, Name = "GetPlayerCustomOp", RootField = "player")]
public sealed class FieldRenamedRequest : TypedRequest<FieldRenamedPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
