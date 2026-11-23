using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLType("Folder")]
public sealed record CycleFolder(string Id, IReadOnlyList<CycleFile> Files);
