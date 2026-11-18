using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Player")]
public sealed record ConditionallyIgnoredPlayer(
    string Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level
);
