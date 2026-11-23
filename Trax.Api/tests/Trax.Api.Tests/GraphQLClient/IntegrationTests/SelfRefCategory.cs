using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Category")]
public sealed record SelfRefCategory(string Id, string Name, SelfRefCategory? Parent);
