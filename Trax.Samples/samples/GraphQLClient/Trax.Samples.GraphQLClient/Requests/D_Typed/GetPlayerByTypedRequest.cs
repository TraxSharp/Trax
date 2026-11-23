using Trax.Api.GraphQL.Client.Typed;
using GraphQLType = Trax.Api.GraphQL.Client.Typed.GraphQLTypeAttribute;

namespace Trax.Samples.GraphQLClient.Requests.D_Typed;

[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerByTypedRequest : TypedRequest<TypedPlayerProfile>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
