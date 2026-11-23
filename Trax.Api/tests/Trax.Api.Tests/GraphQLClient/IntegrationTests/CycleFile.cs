using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("File")]
public sealed record CycleFile(string Id, CycleFolder Folder);
