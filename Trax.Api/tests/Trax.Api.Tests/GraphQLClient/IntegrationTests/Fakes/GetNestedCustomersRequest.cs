using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "typedCustomers")]
public sealed class GetNestedCustomersRequest : TypedRequest<IReadOnlyList<TypedItemSummary>>
{
    // No arguments — exercises the wrapper emission with an argumentless inner field.
}
