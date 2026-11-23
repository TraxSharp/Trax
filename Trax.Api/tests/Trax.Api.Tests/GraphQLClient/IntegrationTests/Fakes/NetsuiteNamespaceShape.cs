using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLType("NetsuiteQueries")]
public sealed record NetsuiteNamespaceShape(IReadOnlyList<TypedItemSummary> TypedCustomers);
