using FluentAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// The operation-name parser in OutboundQueryDiscovery handles a few edge shapes that the
/// happy-path tests don't cover. These exercise: anonymous queries (no name), mutations,
/// queries that throw during property access, and abstract/non-class types being skipped.
/// </summary>
[TestFixture]
public class OutboundQueryDiscoveryEdgePathsTests
{
    [Test]
    public void Discover_AnonymousQuery_ReturnsNullQueryName()
    {
        var entries = OutboundQueryDiscovery.Discover(typeof(AnonymousQuery).Assembly);

        entries
            .Single(e => e.RequestType == typeof(AnonymousQuery))
            .QueryName.Should()
            .BeNull("the query has no identifier after 'query', so QueryName cannot be parsed");
    }

    [Test]
    public void Discover_QueryWithImmediateSelectionSet_ReturnsNullQueryName()
    {
        // `query { ... }` is valid GraphQL but has no operation name. Tests the `rest[0] == '{'`
        // bailout in ExtractOperationName.
        var entries = OutboundQueryDiscovery.Discover(typeof(InlineQuery).Assembly);

        entries.Single(e => e.RequestType == typeof(InlineQuery)).QueryName.Should().BeNull();
    }

    [Test]
    public void Discover_MutationKeyword_IsParsedToo()
    {
        // The parser must recognize "mutation" as well as "query"; without this test, a
        // regression that only handled queries would silently drop mutations from the
        // dashboard's outbound-dependency view.
        var entries = OutboundQueryDiscovery.Discover(typeof(NamedMutation).Assembly);

        entries
            .Single(e => e.RequestType == typeof(NamedMutation))
            .QueryName.Should()
            .Be("RenameSomething");
    }

    [Test]
    public void Discover_QueryThatThrowsOnQueryAccess_StillReportsEntry()
    {
        // If a request type's Query property throws on uninitialized access, the discovery
        // should still record the (type, endpoint) entry - just with a null QueryName.
        // Losing the entry entirely would silently hide a real dependency.
        var entries = OutboundQueryDiscovery.Discover(typeof(ThrowingQuery).Assembly);

        entries.Single(e => e.RequestType == typeof(ThrowingQuery)).QueryName.Should().BeNull();
    }

    [Test]
    public void Discover_AbstractRequestType_IsSkipped()
    {
        // An abstract base implementing IGenericGraphQLClientRequest should never be reported -
        // it's a template, not a real request. Without this filter the dashboard would show
        // phantom dependencies.
        var entries = OutboundQueryDiscovery.Discover(typeof(AbstractTaggedRequest).Assembly);

        entries.Should().NotContain(e => e.RequestType == typeof(AbstractTaggedRequest));
    }
}

[TraxOutboundQuery("Anon")]
file sealed class AnonymousQuery : IGraphQLClientRequest<object>
{
    public string Query => "query { __typename }";
}

[TraxOutboundQuery("Inline")]
file sealed class InlineQuery : IGraphQLClientRequest<object>
{
    public string Query => "query    {  __typename }";
}

[TraxOutboundQuery("Mutations")]
file sealed class NamedMutation : IGraphQLClientRequest<object>
{
    public string Query => "mutation RenameSomething { __typename }";
}

[TraxOutboundQuery("Throws")]
file sealed class ThrowingQuery : IGraphQLClientRequest<object>
{
    public string Query => throw new InvalidOperationException("query getter explodes");
}

[TraxOutboundQuery("AbstractBase")]
file abstract class AbstractTaggedRequest : IGraphQLClientRequest<object>
{
    public abstract string Query { get; }
}
