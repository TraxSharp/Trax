using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLOperation(OperationType.Query, RootField = "category")]
public sealed class GetCategoryRequest : TypedRequest<SelfRefCategory>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
