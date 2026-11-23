using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLType("Guild")]
public sealed record TypedGuildSummary(string Id, string Name);
