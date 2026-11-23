using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Player")]
public sealed record AliasedPlayer(
    string Id,
    [property: GraphQLField("name")] string DisplayName,
    int? Level
);
