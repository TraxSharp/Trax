using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Player")]
public sealed record BadAliasPlayer(
    string Id,
    [property:
        GraphQLField("name"),
        System.Text.Json.Serialization.JsonPropertyName("display-name")
    ]
        string DisplayName
);
