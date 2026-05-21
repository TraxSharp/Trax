using System.Text.Json;
using FluentAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Direct tests of <see cref="TypedRequest{T}"/>.Extract over nested-path responses. The
/// executor end-to-end tests cover the strictness-validator interaction; these pin down
/// the navigation logic itself: missing fields, null leaves, wrong shapes, and arbitrary
/// depths. Without these, a regression that silently returns default(T) for a missing
/// intermediate step would only surface as a confusing NullReferenceException downstream.
/// </summary>
[TestFixture]
public class TypedRequestPathExtractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Test]
    public void Extract_NoPath_DescendsIntoRootField()
    {
        // Flat behavior is unchanged: { player: {...} } -> the Player object.
        var data = ParseData(
            """
            { "player": { "id": "p1", "name": "Aragorn", "level": 42, "rank": "GOLD", "guild": null, "inventory": [] } }
            """
        );

        IGraphQLClientRequest<TypedPlayerProfile> request = new GetPlayerByTypedRequest
        {
            Id = "ignored",
        };
        var result = request.Extract(data, Options);

        result.Id.Should().Be("p1");
        result.Name.Should().Be("Aragorn");
        result.Level.Should().Be(42);
    }

    [Test]
    public void Extract_TwoSegmentPath_WalksThreeLevels()
    {
        var data = ParseData(
            """
            { "discover": { "netsuite": { "typedCustomer": { "id": "c1", "name": "Acme", "level": null, "rank": "BRONZE", "guild": null, "inventory": [] } } } }
            """
        );

        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "acme",
        };
        var result = request.Extract(data, Options);

        result.Should().NotBeNull();
        result!.Id.Should().Be("c1");
        result.Name.Should().Be("Acme");
    }

    [Test]
    public void Extract_PathLeafIsNull_ReturnsNull()
    {
        // The query is `discover.netsuite.typedCustomer` returning a nullable Player. When
        // the leaf field comes back as JSON null, Extract must propagate null rather than
        // throwing - this mirrors how the GraphQL spec treats a null result on a nullable field.
        var data = ParseData("""{ "discover": { "netsuite": { "typedCustomer": null } } }""");

        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "missing",
        };
        var result = request.Extract(data, Options);

        result.Should().BeNull();
    }

    [Test]
    public void Extract_MissingPathStep_ThrowsWithFieldAndRequestType()
    {
        // The middle wrapper is missing. Error must name the missing field AND the request
        // type so a consumer can find both the schema mismatch and the offending POCO.
        var data = ParseData("""{ "discover": { } }""");
        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("netsuite");
        ex.Message.Should().Contain("GetNestedCustomerByEmailRequest");
    }

    [Test]
    public void Extract_MissingPathStep_ListsAvailableFields()
    {
        // The error must show what WAS present at that level so the consumer can
        // diff their Path against the actual schema without re-querying the server.
        var data = ParseData("""{ "discover": { "players": { }, "other": null } }""");
        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("players");
        ex.Message.Should().Contain("other");
    }

    [Test]
    public void Extract_NullIntermediateStep_ThrowsCleanly()
    {
        // A non-leaf step that comes back null is a server-side bug or schema misconfig:
        // wrapper fields are always non-null. Surface it as a clear extraction failure
        // rather than letting the next walk step blow up with NullReferenceException.
        var data = ParseData("""{ "discover": null }""");
        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("discover");
        ex.Message.Should().Contain("GetNestedCustomerByEmailRequest");
        ex.Message.Should().Contain("null");
    }

    [Test]
    public void Extract_NonObjectAtPathStep_ThrowsCleanly()
    {
        // The server sent a scalar where the extractor expected to descend. This is a
        // shape mismatch worth a clear error so the consumer sees the actual JSON type.
        var data = ParseData("""{ "discover": 42 }""");
        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("discover");
        ex.Message.Should().Contain("Number");
        ex.Message.Should().Contain("GetNestedCustomerByEmailRequest");
    }

    [Test]
    public void Extract_ListLeaf_DeserializesCorrectly()
    {
        // Nested path + list-returning root field. The walker must descend through the
        // wrappers, then hand the LIST element to the deserializer (not unwrap it further).
        var data = ParseData(
            """
            { "discover": { "netsuite": { "typedCustomers": [ { "id": "i1", "name": "Acme", "category": "CONSUMABLE" }, { "id": "i2", "name": "Beta", "category": "CONSUMABLE" } ] } } }
            """
        );

        IGraphQLClientRequest<IReadOnlyList<TypedItemSummary>> request =
            new GetNestedCustomersRequest();
        var result = request.Extract(data, Options);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("i1");
        result[1].Id.Should().Be("i2");
    }

    [Test]
    public void Extract_NoPath_MissingRootField_ThrowsNamingEnvelope()
    {
        // Real-world regression: server renames a root field without notifying the client.
        // The flat typed request has no Path, so the error message refers to the top-level
        // data envelope rather than a named parent field — exercises the `parentField is null`
        // arm of the error builder that the Path-based failure tests don't reach.
        var data = ParseData("""{ "wrongFieldName": { "id": "p1" } }""");
        IGraphQLClientRequest<TypedPlayerProfile> request = new GetPlayerByTypedRequest
        {
            Id = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("player"); // the RootField the client expected
        ex.Message.Should().Contain("response data envelope");
        ex.Message.Should().Contain("wrongFieldName"); // what was actually present
        ex.Message.Should().Contain("GetPlayerByTypedRequest");
    }

    [Test]
    public void Extract_NonObjectDataEnvelope_NamesTheEnvelopeInError()
    {
        // Buggy server returns `{"data": 42}` instead of an object. The executor's
        // pre-Extract filter only blocks Undefined and Null kinds, so a malformed envelope
        // reaches our extractor. The error must name the envelope (not a fictional parent
        // field) and the actual JSON kind so the operator can debug the server, not us.
        var data = ParseData("42");
        IGraphQLClientRequest<TypedPlayerProfile> request = new GetPlayerByTypedRequest
        {
            Id = "x",
        };

        var act = () => request.Extract(data, Options);

        var ex = act.Should().Throw<GraphQLExecutionException>().Which;
        ex.Message.Should().Contain("response data envelope");
        ex.Message.Should().Contain("Number");
        ex.Message.Should().Contain("GetPlayerByTypedRequest");
    }

    [Test]
    public void UsesDefaultExtractor_NestedRequest_StaysTrue()
    {
        // Strictness validation only runs for requests that say so. A nested-path request
        // is still using the framework-provided extractor, so it should NOT opt out of
        // shape validation - the executor needs to know it can apply ThrowOnDrift checks.
        IGraphQLClientRequest<TypedPlayerProfile?> request = new GetNestedCustomerByEmailRequest
        {
            Email = "x",
        };

        request.UsesDefaultExtractor.Should().BeTrue();
    }

    private static JsonElement ParseData(string json)
    {
        // JsonDocument cloning is required because Parse returns a disposable doc, but
        // Extract is allowed to hold onto the element across method calls.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
