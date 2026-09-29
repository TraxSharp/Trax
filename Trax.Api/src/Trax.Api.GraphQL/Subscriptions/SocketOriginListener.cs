using HotChocolate.AspNetCore.Instrumentation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Applies <see cref="SocketOriginPolicy"/> to every WebSocket session the Trax schema serves,
/// however the host mapped it.
/// </summary>
/// <remarks>
/// HotChocolate raises <see cref="WebSocketSession"/> on the schema's own listeners before it
/// accepts the upgrade, so a listener registered on the Trax schema sees every socket that schema
/// is about to serve: through <c>UseTraxGraphQL()</c>, or through a host's own
/// <c>MapGraphQL(path, "trax")</c>, and never a socket for another schema. When the origin is not
/// allowed, the listener swaps the request's <see cref="IHttpWebSocketFeature"/> for one whose
/// accept answers <c>403</c> instead of switching protocols, so the handshake never completes.
/// HotChocolate treats the refused accept as a session error and ends the request.
/// See <c>docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md</c>.
/// </remarks>
internal sealed class SocketOriginListener : ServerDiagnosticEventListener
{
    public override IDisposable WebSocketSession(HttpContext context)
    {
        var allowedOrigins = context
            .RequestServices.GetService<GraphQLConfiguration>()
            ?.SocketAllowedOrigins;

        if (!SocketOriginPolicy.IsAllowed(context, allowedOrigins))
            context.Features.Set<IHttpWebSocketFeature>(new RefusedUpgrade(context));

        return EmptyScope;
    }

    /// <summary>
    /// Stands in for the WebSocket feature on a refused upgrade: still an upgrade request, but
    /// accepting it sets <c>403</c> and throws instead of switching protocols.
    /// </summary>
    private sealed class RefusedUpgrade(HttpContext context) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(
            WebSocketAcceptContext acceptContext
        )
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

            throw new SocketUpgradeRefusedException();
        }
    }
}

/// <summary>
/// Thrown when a WebSocket upgrade to the Trax schema comes from an origin that is not allowed.
/// HotChocolate reports it as a session error; the response is <c>403</c>.
/// </summary>
internal sealed class SocketUpgradeRefusedException()
    : InvalidOperationException("The WebSocket upgrade origin is not allowed for this endpoint.");
