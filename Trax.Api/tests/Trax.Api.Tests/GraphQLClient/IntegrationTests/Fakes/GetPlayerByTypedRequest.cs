using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerByTypedRequest : TypedRequest<TypedPlayerProfile>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
