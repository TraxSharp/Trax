using System.Text.Json;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// NSubstitute-based stand-ins for HotChocolate's socket abstractions. HC's
/// concrete types are internal and bound to JSON parsing of real socket
/// frames, so unit tests fake them at the interface boundary.
/// </summary>
internal static class SocketInterceptorTestHelpers
{
    /// <summary>
    /// Builds a payload carrying the JSON a real client would send for
    /// <paramref name="value"/>: camelCase keys, serialized and re-parsed so the
    /// interceptor exercises its own deserialization rather than a stubbed result.
    /// </summary>
    public static IOperationMessagePayload Payload<T>(T value)
        where T : class => RawPayload(JsonSerializer.Serialize(value, WebOptions));

    /// <summary>
    /// Builds a payload from a literal JSON document, for casing and malformed-input
    /// cases that a serialized object cannot express.
    /// </summary>
    public static IOperationMessagePayload RawPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        var payload = Substitute.For<IOperationMessagePayload>();
        payload.Payload.Returns(document.RootElement.Clone());
        return payload;
    }

    /// <summary>
    /// Builds a payload with no JSON at all — the frame carried no <c>payload</c> member.
    /// </summary>
    public static IOperationMessagePayload EmptyPayload()
    {
        var payload = Substitute.For<IOperationMessagePayload>();
        payload.Payload.Returns((JsonElement?)null);
        return payload;
    }

    /// <summary>
    /// Wraps <paramref name="resolver"/> in an application container the interceptor can open a
    /// scope against, registered <b>scoped</b> to match how a host registers it.
    /// </summary>
    /// <remarks>
    /// The interceptor is a singleton and cannot hold a scoped resolver, so it takes the container
    /// and resolves per connection. Registering the resolver scoped here means these tests fail the
    /// same way production did if that ever regresses.
    /// </remarks>
    public static TraxApplicationServices AppServicesWith<TInput>(
        ITraxPrincipalResolver<TInput> resolver
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => resolver);
        return new TraxApplicationServices(
            services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            )
        );
    }

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds a fake <see cref="ISocketSession"/> wrapping a
    /// <see cref="DefaultHttpContext"/>. Returns both so the test can assert
    /// on the principal attached to HttpContext.User.
    /// </summary>
    public static (ISocketSession Session, HttpContext HttpContext) NewSession()
    {
        var http = new DefaultHttpContext();
        var connection = Substitute.For<ISocketConnection>();
        connection.HttpContext.Returns(http);
        var session = Substitute.For<ISocketSession>();
        session.Connection.Returns(connection);
        return (session, http);
    }
}
