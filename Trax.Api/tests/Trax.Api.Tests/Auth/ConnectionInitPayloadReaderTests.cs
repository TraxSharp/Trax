using FluentAssertions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// The <c>connection_init</c> payload arrives as raw JSON, so the reader is the only thing
/// standing between a client's frame and a typed token. HotChocolate 15 did the
/// deserialization itself; version 16 hands over the JSON, which makes the casing and
/// malformed-input behaviour Trax's to get right.
/// </summary>
[TestFixture]
public class ConnectionInitPayloadReaderTests
{
    private sealed record Credentials(string? AuthToken, string? ApiKey);

    [Test]
    public void TryRead_CamelCaseKeys_Deserializes()
    {
        // What every graphql-transport-ws client actually sends.
        var payload = RawPayload("""{"authToken":"t","apiKey":"k"}""");

        var result = ConnectionInitPayloadReader.TryRead<Credentials>(payload);

        result.Should().Be(new Credentials("t", "k"));
    }

    [Test]
    public void TryRead_PascalCaseKeys_AlsoDeserializes()
    {
        // Hand-rolled clients sometimes serialize with .NET's default naming.
        var payload = RawPayload("""{"AuthToken":"t","ApiKey":"k"}""");

        var result = ConnectionInitPayloadReader.TryRead<Credentials>(payload);

        result.Should().Be(new Credentials("t", "k"));
    }

    [Test]
    public void TryRead_MissingMembers_LeavesThemNull()
    {
        var payload = RawPayload("""{"authToken":"t"}""");

        var result = ConnectionInitPayloadReader.TryRead<Credentials>(payload);

        result!.AuthToken.Should().Be("t");
        result.ApiKey.Should().BeNull();
    }

    [Test]
    public void TryRead_UnknownMembers_AreIgnored()
    {
        var payload = RawPayload("""{"authToken":"t","somethingElse":42}""");

        ConnectionInitPayloadReader.TryRead<Credentials>(payload)!.AuthToken.Should().Be("t");
    }

    [Test]
    public void TryRead_EmptyObject_ReturnsAllNullMembers()
    {
        var payload = RawPayload("{}");

        ConnectionInitPayloadReader
            .TryRead<Credentials>(payload)
            .Should()
            .Be(new Credentials(null, null));
    }

    [Test]
    public void TryRead_NoPayload_ReturnsNull()
    {
        ConnectionInitPayloadReader.TryRead<Credentials>(EmptyPayload()).Should().BeNull();
    }

    [Test]
    public void TryRead_JsonNull_ReturnsNull()
    {
        ConnectionInitPayloadReader.TryRead<Credentials>(RawPayload("null")).Should().BeNull();
    }

    [Test]
    public void TryRead_WrongShape_ReturnsNullInsteadOfThrowing()
    {
        // An array where an object was expected: the interceptors treat this exactly like a
        // missing payload and reject the connection, rather than surfacing a JsonException
        // through the socket handshake.
        var payload = RawPayload("""["not","an","object"]""");

        ConnectionInitPayloadReader.TryRead<Credentials>(payload).Should().BeNull();
    }

    [Test]
    public void TryRead_MemberOfWrongType_ReturnsNullInsteadOfThrowing()
    {
        var payload = RawPayload("""{"authToken":123}""");

        ConnectionInitPayloadReader.TryRead<Credentials>(payload).Should().BeNull();
    }

    [Test]
    public void TryRead_RoundTripsASerializedObject()
    {
        var payload = Payload(new Credentials("round", "trip"));

        ConnectionInitPayloadReader
            .TryRead<Credentials>(payload)
            .Should()
            .Be(new Credentials("round", "trip"));
    }
}
