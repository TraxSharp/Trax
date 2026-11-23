using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Match")]
public sealed record SiblingMatch(string Id, MatchTeam Home, MatchTeam Away);
