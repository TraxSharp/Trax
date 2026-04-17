using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// NSubstitute-based stand-ins for HotChocolate's socket abstractions. HC's
/// concrete types are internal and bound to JSON parsing of real socket
/// frames, so unit tests fake them at the interface boundary.
/// </summary>
internal static class SocketInterceptorTestHelpers
{
    /// <summary>
    /// Builds a fake <see cref="IOperationMessagePayload"/> that returns the
    /// given value from <c>As&lt;T&gt;()</c> when T matches the supplied
    /// type. Any other T returns null. Simulates a well-formed JSON payload
    /// that deserializes only to the expected shape.
    /// </summary>
    public static IOperationMessagePayload Payload<T>(T value)
        where T : class
    {
        var payload = Substitute.For<IOperationMessagePayload>();
        payload.As<T>().Returns(value);
        return payload;
    }

    /// <summary>
    /// Builds a fake payload where <c>As&lt;T&gt;()</c> returns null — simulates
    /// a missing or malformed payload.
    /// </summary>
    public static IOperationMessagePayload EmptyPayload() =>
        Substitute.For<IOperationMessagePayload>();

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
