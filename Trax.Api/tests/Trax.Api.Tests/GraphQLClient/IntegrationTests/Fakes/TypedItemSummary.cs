using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLType("Item")]
public sealed record TypedItemSummary(string Id, string Name, string Category);
