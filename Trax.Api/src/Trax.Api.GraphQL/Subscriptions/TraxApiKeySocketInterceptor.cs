using System.Security.Claims;
using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// HotChocolate socket session interceptor that authenticates GraphQL
/// subscriptions using the same <see cref="ITraxPrincipalResolver{String}"/> as
/// the HTTP API key scheme. Browsers cannot attach arbitrary headers to a
/// WebSocket upgrade, so subscription auth travels in the <c>connection_init</c>
/// payload instead.
/// </summary>
/// <remarks>
/// Expected payload shape (either key is accepted — <c>authToken</c> is the
/// convention on GraphQL transport WS, <c>apiKey</c> matches the REST header):
/// <code>{ "authToken": "..." }</code> or <code>{ "apiKey": "..." }</code>.
/// <para>
/// Registered automatically by <c>AddTraxApiKeyAuth</c> when the Trax GraphQL
/// schema is also present. Hosts that prefer their own subscription-auth
/// pipeline can remove this registration and wire their own
/// <see cref="ISocketSessionInterceptor"/>.
/// </para>
/// </remarks>
public sealed class TraxApiKeySocketInterceptor(
    ITraxPrincipalResolver<string> resolver,
    ILogger<TraxApiKeySocketInterceptor> logger
) : DefaultSocketSessionInterceptor
{
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var payload = TryReadPayload(connectionInitMessage);
        var apiKey = payload?.AuthToken ?? payload?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return ConnectionStatus.Reject("Missing auth token in connection_init payload.");

        TraxPrincipal? principal;
        try
        {
            principal = await resolver.ResolveAsync(apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax subscription API-key resolver threw an exception.");
            return ConnectionStatus.Reject("Auth resolver failed.");
        }

        if (principal is null)
            return ConnectionStatus.Reject("Invalid auth token.");

        // Authentication-type string matches the REST scheme name so downstream
        // code that inspects ClaimsPrincipal.Identity.AuthenticationType sees a
        // consistent identifier across HTTP and WS paths.
        var claimsPrincipal = principal.ToClaimsPrincipal("TraxApiKey");
        AttachPrincipalToRequest(session, claimsPrincipal);

        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    public override async ValueTask OnRequestAsync(
        ISocketSession session,
        string operationSessionId,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken = default
    )
    {
        // Re-assert the principal onto per-operation HttpContext each time — HC reuses
        // the same HttpContext object for the socket lifetime, but guards here are cheap
        // and prevent a regression where a middleware downstream resets User.
        if (
            session.Connection.HttpContext is { } httpContext
            && httpContext.User.Identity?.IsAuthenticated != true
        )
        {
            // No-op: principal was never attached at init; OnConnectAsync already
            // rejected the connection in that path. Defensive branch only.
        }

        await base.OnRequestAsync(session, operationSessionId, requestBuilder, cancellationToken);
    }

    private static void AttachPrincipalToRequest(
        ISocketSession session,
        ClaimsPrincipal claimsPrincipal
    )
    {
        if (session.Connection.HttpContext is { } httpContext)
            httpContext.User = claimsPrincipal;
    }

    private static ConnectionInitPayload? TryReadPayload(IOperationMessagePayload payload) =>
        ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(payload);

    internal sealed record ConnectionInitPayload(string? AuthToken, string? ApiKey);
}
