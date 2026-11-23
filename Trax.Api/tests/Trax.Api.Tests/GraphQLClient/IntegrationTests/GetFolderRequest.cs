using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLOperation(OperationType.Query, RootField = "folder")]
public sealed class GetFolderRequest : TypedRequest<CycleFolder>;
