using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

[TraxOutboundQuery("Mutations")]
internal sealed class NamedMutation : IGraphQLClientRequest<object>
{
    public string Query => "mutation RenameSomething { __typename }";
}
