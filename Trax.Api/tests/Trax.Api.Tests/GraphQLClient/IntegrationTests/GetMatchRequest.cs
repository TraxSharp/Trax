using AwesomeAssertions;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

[GraphQLOperation(OperationType.Query, RootField = "match")]
public sealed class GetMatchRequest : TypedRequest<SiblingMatch>;
