using System.Security.Claims;
using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Extensions;

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
/// <c>AddTraxGraphQL</c> does not register this type itself: it registers
/// <see cref="TraxCompositeSocketInterceptor"/>, which delegates API-key
/// connections here whenever <c>AddTraxApiKeyAuth</c> is registered, alongside
/// JWT or on its own. Hosts that prefer their own subscription-auth pipeline
/// register their own <see cref="ISocketSessionInterceptor"/> through
/// <c>ConfigureSchema</c>, which replaces the composite.
/// </para>
/// </remarks>
public sealed class TraxApiKeySocketInterceptor(
    TraxApplicationServices applicationServices,
    ILogger<TraxApiKeySocketInterceptor> logger
) : DefaultSocketSessionInterceptor
{
    /// <summary>
    /// Accepts the connection when the <c>connection_init</c> payload carries an API key that the
    /// registered resolver maps to a principal, and rejects it otherwise.
    /// </summary>
    /// <param name="session">The socket session being opened.</param>
    /// <param name="connectionInitMessage">The <c>connection_init</c> message and its payload.</param>
    /// <param name="cancellationToken">Cancels resolution.</param>
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
            // The resolver is scoped: it is the host's code and typically hits a database.
            // A socket interceptor is a singleton, so it cannot hold one, and the connection
            // gets its own scope instead.
            await using var scope = applicationServices.Services.CreateAsyncScope();
            var resolver = scope.ServiceProvider.GetRequiredService<
                ITraxPrincipalResolver<string>
            >();
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
