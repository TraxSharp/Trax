using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLOperation(OperationType.Query, Name = "ConditionallyIgnoredPlayer", RootField = "player")]
public sealed class ConditionallyIgnoredPlayerRequest : TypedRequest<ConditionallyIgnoredPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
