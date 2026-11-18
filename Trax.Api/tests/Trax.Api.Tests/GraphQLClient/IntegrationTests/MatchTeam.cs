using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Team")]
public sealed record MatchTeam(string Id, string Name);
