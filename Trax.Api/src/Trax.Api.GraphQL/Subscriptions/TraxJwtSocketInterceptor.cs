using System.Security.Claims;
using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// HotChocolate socket session interceptor that authenticates GraphQL
/// subscriptions using JWT bearer credentials carried in the
/// <c>connection_init</c> payload. Browsers cannot attach arbitrary headers
/// to a WebSocket upgrade, so the token travels in the handshake payload
/// instead.
/// </summary>
/// <remarks>
/// Expected payload shape (either key is accepted — <c>authToken</c> is the
/// graphql-transport-ws convention, <c>bearer</c> is accepted for clients
/// that prefer naming it after the HTTP header):
/// <code>{ "authToken": "eyJ..." }</code> or <code>{ "bearer": "eyJ..." }</code>.
/// <para>
/// Registered automatically by <c>AddTraxGraphQL</c> when
/// <c>ITraxPrincipalResolver&lt;JwtTokenInput&gt;</c> is present. Hosts that
/// prefer their own subscription-auth pipeline can remove the registration
/// and wire a custom <see cref="ISocketSessionInterceptor"/>.
/// </para>
/// Token validation reuses the same <see cref="JwtBearerOptions"/> the HTTP
/// handler uses (signature, issuer, audience, lifetime, clock skew), so the
/// WS and HTTP paths cannot diverge.
/// </remarks>
public sealed class TraxJwtSocketInterceptor(
    IOptionsMonitor<JwtBearerOptions> optionsMonitor,
    ITraxPrincipalResolver<JwtTokenInput> resolver,
    ILogger<TraxJwtSocketInterceptor> logger
) : DefaultSocketSessionInterceptor
{
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var payload = TryReadPayload(connectionInitMessage);
        var token = payload?.AuthToken ?? payload?.Bearer;
        if (string.IsNullOrWhiteSpace(token))
            return ConnectionStatus.Reject("Missing auth token in connection_init payload.");

        var options = optionsMonitor.Get(JwtDefaults.SchemeName);

        TokenValidationResult validation;
        try
        {
            validation = await ValidateAsync(token, options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax subscription JWT validation threw an exception.");
            return ConnectionStatus.Reject("JWT validation failed.");
        }

        if (!validation.IsValid || validation.ClaimsIdentity is null)
            return ConnectionStatus.Reject("Invalid JWT.");

        var validatedPrincipal = new ClaimsPrincipal(validation.ClaimsIdentity);
        var input = new JwtTokenInput(validatedPrincipal, validation.SecurityToken);

        TraxPrincipal? traxPrincipal;
        try
        {
            traxPrincipal = await resolver.ResolveAsync(input, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax subscription JWT resolver threw an exception.");
            return ConnectionStatus.Reject("JWT resolver failed.");
        }

        if (traxPrincipal is null)
            return ConnectionStatus.Reject("JWT did not map to a known Trax principal.");

        var claimsPrincipal = traxPrincipal.ToClaimsPrincipal(JwtDefaults.SchemeName);
        AttachPrincipalToRequest(session, claimsPrincipal);

        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    private static async Task<TokenValidationResult> ValidateAsync(
        string token,
        JwtBearerOptions options
    )
    {
        // Reuse JsonWebTokenHandler, the modern validator JwtBearerHandler uses.
        // TokenValidationParameters carry issuer/audience/key/clock-skew from
        // the same options the HTTP handler validates against, so WS and HTTP
        // paths cannot diverge.
        var handler = new JsonWebTokenHandler();
        return await handler.ValidateTokenAsync(token, options.TokenValidationParameters);
    }

    private static void AttachPrincipalToRequest(
        ISocketSession session,
        ClaimsPrincipal claimsPrincipal
    )
    {
        if (session.Connection.HttpContext is { } httpContext)
            httpContext.User = claimsPrincipal;
    }

    private static ConnectionInitPayload? TryReadPayload(IOperationMessagePayload payload)
    {
        try
        {
            return payload.As<ConnectionInitPayload>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record ConnectionInitPayload(string? AuthToken, string? Bearer);
}
