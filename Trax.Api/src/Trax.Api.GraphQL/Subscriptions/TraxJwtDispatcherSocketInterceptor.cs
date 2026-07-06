using System.Security.Claims;
using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// HotChocolate socket session interceptor that authenticates GraphQL
/// subscriptions across multiple JWT schemes, routing by the token's <c>iss</c>
/// claim through the same <c>AddTraxJwtDispatcher</c> mapping the HTTP path uses.
/// Wired automatically by <c>AddTraxGraphQL</c> when a dispatcher is registered,
/// replacing the single-scheme <see cref="TraxJwtSocketInterceptor"/>.
/// </summary>
/// <remarks>
/// The issuer is read from the token without validating its signature and is used
/// only to select a scheme. The selected scheme then validates signature, issuer,
/// audience, and lifetime (fetching JWKS keys via its OIDC discovery document when
/// needed), so an attacker cannot bypass validation by forging <c>iss</c>.
/// </remarks>
public sealed class TraxJwtDispatcherSocketInterceptor(
    JwtDispatcherRuntime dispatcher,
    IOptionsMonitor<JwtBearerOptions> optionsMonitor,
    IServiceProvider services,
    ILogger<TraxJwtDispatcherSocketInterceptor> logger
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

        var scheme = dispatcher.ResolveSchemeForToken(token);
        if (scheme is null)
            return ConnectionStatus.Reject("Token issuer is not recognized.");

        var options = optionsMonitor.Get(scheme);

        TokenValidationResult validation;
        try
        {
            validation = await JwtSocketTokenValidator.ValidateAsync(
                token,
                options,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax dispatcher subscription JWT validation threw for scheme {Scheme}.",
                scheme
            );
            return ConnectionStatus.Reject("JWT validation failed.");
        }

        if (!validation.IsValid || validation.ClaimsIdentity is null)
            return ConnectionStatus.Reject("Invalid JWT.");

        var validatedPrincipal = new ClaimsPrincipal(validation.ClaimsIdentity);
        var input = new JwtTokenInput(validatedPrincipal, validation.SecurityToken);

        // Named-scheme resolvers are registered scoped, and a socket connection has
        // no ambient request scope, so resolve the resolver in a fresh scope.
        await using var scope = services.CreateAsyncScope();
        var resolver = dispatcher.ResolvePrincipalResolver(scheme, scope.ServiceProvider);
        if (resolver is null)
            return ConnectionStatus.Reject(
                $"No principal resolver is registered for scheme '{scheme}'."
            );

        TraxPrincipal? traxPrincipal;
        try
        {
            traxPrincipal = await resolver.ResolveAsync(input, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax dispatcher subscription JWT resolver threw for scheme {Scheme}.",
                scheme
            );
            return ConnectionStatus.Reject("JWT resolver failed.");
        }

        if (traxPrincipal is null)
            return ConnectionStatus.Reject("JWT did not map to a known Trax principal.");

        var claimsPrincipal = traxPrincipal.ToClaimsPrincipal(scheme);
        if (session.Connection.HttpContext is { } httpContext)
            httpContext.User = claimsPrincipal;

        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
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
