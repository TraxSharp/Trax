using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;

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
/// <see cref="TraxCompositeSocketInterceptor"/> delegates JWT connections here when
/// <c>ITraxPrincipalResolver&lt;JwtTokenInput&gt;</c> is registered and no
/// dispatcher is. Hosts that prefer their own subscription-auth pipeline
/// register a custom <see cref="ISocketSessionInterceptor"/> through
/// <c>ConfigureSchema</c>, which replaces the composite.
/// </para>
/// The token is authenticated by the <c>TraxJwt</c> scheme's own handler, as an HTTP request
/// carrying it would be (see <see cref="JwtSocketAuthentication"/>): signature, issuer, audience,
/// lifetime and clock skew, a JWKS refresh on an unknown key id, and the host's
/// <c>JwtBearerEvents</c>. When <c>AddTraxJwtAuth</c> registered the scheme, its handler has already
/// resolved the Trax principal; otherwise the registered
/// <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> resolves it here. The connection closes when
/// the token expires. See <c>docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md</c>.
/// <para>
/// The constructor's <see cref="IOptionsMonitor{JwtBearerOptions}"/> is no longer read, because
/// the handler reads its own options; it stays so the constructor is unchanged.
/// </para>
/// </remarks>
public sealed class TraxJwtSocketInterceptor : DefaultSocketSessionInterceptor
{
    private readonly TraxApplicationServices _applicationServices;
    private readonly ILogger<TraxJwtSocketInterceptor> _logger;

    /// <summary>Creates the interceptor for the <c>TraxJwt</c> scheme.</summary>
    public TraxJwtSocketInterceptor(
        IOptionsMonitor<JwtBearerOptions> optionsMonitor,
        TraxApplicationServices applicationServices,
        ILogger<TraxJwtSocketInterceptor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(applicationServices);
        ArgumentNullException.ThrowIfNull(logger);
        _applicationServices = applicationServices;
        _logger = logger;
    }

    /// <summary>
    /// Accepts the connection when the <c>connection_init</c> payload carries a JWT that validates
    /// against the registered JWT scheme, and rejects it otherwise.
    /// </summary>
    /// <param name="session">The socket session being opened.</param>
    /// <param name="connectionInitMessage">The <c>connection_init</c> message and its payload.</param>
    /// <param name="cancellationToken">Cancels validation.</param>
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

        const string scheme = JwtDefaults.SchemeName;

        // The resolver is scoped: it is the host's code and typically hits a database. A socket
        // interceptor is a singleton, so it cannot hold one, and the connection gets its own
        // scope instead. The handler and the host's events resolve from the same scope.
        await using var scope = _applicationServices.Services.CreateAsyncScope();

        AuthenticateResult result;
        try
        {
            result = await JwtSocketAuthentication.AuthenticateAsync(
                scope.ServiceProvider,
                session.Connection.HttpContext,
                scheme,
                token,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trax subscription JWT authentication threw an exception.");
            return ConnectionStatus.Reject("JWT validation failed.");
        }

        if (!result.Succeeded || result.Principal is not { } principal)
            return ConnectionStatus.Reject(JwtSocketAuthentication.RejectedMessage);

        // AddTraxJwtAuth's OnTokenValidated resolved the Trax principal inside the handler. A
        // scheme the host registered itself did not, so the resolver runs here.
        var resolvedByHandler =
            _applicationServices.Services.GetService<JwtResolverRegistry>() is { } registry
            && registry.SchemeNames.Contains(scheme);

        if (!resolvedByHandler)
        {
            TraxPrincipal? traxPrincipal;
            try
            {
                var resolver = scope.ServiceProvider.GetRequiredService<
                    ITraxPrincipalResolver<JwtTokenInput>
                >();
                traxPrincipal = await resolver.ResolveAsync(
                    new JwtTokenInput(principal, new JsonWebToken(token)),
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trax subscription JWT resolver threw an exception.");
                return ConnectionStatus.Reject("JWT resolver failed.");
            }

            if (traxPrincipal is null)
                return ConnectionStatus.Reject("JWT did not map to a known Trax principal.");

            principal = traxPrincipal.ToClaimsPrincipal(scheme);
        }

        JwtSocketAuthentication.AttachPrincipal(session, principal);
        JwtSocketAuthentication.CloseAtExpiry(
            session,
            JwtSocketAuthentication.ExpiresAt(result, token),
            _applicationServices.Services,
            _logger
        );

        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    private static ConnectionInitPayload? TryReadPayload(IOperationMessagePayload payload) =>
        ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(payload);

    internal sealed record ConnectionInitPayload(string? AuthToken, string? Bearer);
}
