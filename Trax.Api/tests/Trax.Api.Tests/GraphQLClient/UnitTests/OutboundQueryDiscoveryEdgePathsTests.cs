using FluentAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

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

    [Test]
    public void Discover_TypedRequestWithPath_ParsesOuterOperationName()
    {
        // Nested-path requests still produce a top-level `query Op($var: T!) { ... }` signature,
        // so the discovery's operation-name parser must continue to pick them up. A regression
        // that treated path-wrappers as if they ate the operation name would silently drop
        // every typed Trax-targeting request from the dashboard's outbound view.
        var entries = OutboundQueryDiscovery.Discover(typeof(NestedPathRequest).Assembly);

        entries
            .Single(e => e.RequestType == typeof(NestedPathRequest))
            .QueryName.Should()
            .Be("NestedPath");
    }

    [Test]
    public void Generator_NestedPathFakeProducesQuery()
    {
        // Diagnostic: surface whatever the generator throws so we know whether the discovery
        // failure is upstream (Generate exception) or downstream (parser doesn't match).
        var generated = TypedQueryGenerator.Generate(
            typeof(NestedPathRequest),
            typeof(NestedPathPayload)
        );
        generated.Query.Should().StartWith("query NestedPath");
    }
}

// Non-file scope: the `file` modifier mangles Type.Name with a per-file hash, which would
// break TypedQueryGenerator's name-based operation derivation (StripRequestSuffix). Other
// fakes in this file are file-scoped because they hard-code their Query strings and don't
// go through the generator.
[TraxOutboundQuery("Nested")]
[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "thing")]
internal sealed class NestedPathRequest : TypedRequest<NestedPathPayload>
{
    // No-op; the discovery only reads the Query string.
}

[GraphQLType("Thing")]
internal sealed record NestedPathPayload(string Id);

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
