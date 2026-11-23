using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, RootField = "allItems")]
public sealed class AllItemsByTypedRequest : TypedRequest<IReadOnlyList<TypedItemSummary>>
{
    // No arguments; AllItems takes none.
}
