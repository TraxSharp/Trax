using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// HotChocolate socket session interceptor that authenticates GraphQL
/// subscriptions across multiple JWT schemes, routing by the token's <c>iss</c>
/// claim through the same <c>AddTraxJwtDispatcher</c> mapping the HTTP path uses.
/// <see cref="TraxCompositeSocketInterceptor"/> delegates JWT connections here when
/// a dispatcher is registered, in place of the single-scheme
/// <see cref="TraxJwtSocketInterceptor"/>.
/// </summary>
/// <remarks>
/// The issuer is read from the token without validating its signature and is used only to select
/// a scheme. The selected scheme's own handler then authenticates the token as it would an HTTP
/// request carrying it (see <see cref="JwtSocketAuthentication"/>): signature, issuer, audience and
/// lifetime, a JWKS refresh on an unknown key id, the host's <c>JwtBearerEvents</c>, and Trax's
/// principal resolution. Forging <c>iss</c> therefore selects a scheme but bypasses nothing. The
/// connection closes when the token expires. See
/// <c>docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md</c>.
/// <para>
/// The constructor's <see cref="IOptionsMonitor{JwtBearerOptions}"/> is no longer read, because
/// the handler reads its own options; it stays so the constructor is unchanged.
/// </para>
/// </remarks>
public sealed class TraxJwtDispatcherSocketInterceptor : DefaultSocketSessionInterceptor
{
    private readonly JwtDispatcherRuntime _dispatcher;
    private readonly TraxApplicationServices _applicationServices;
    private readonly ILogger<TraxJwtDispatcherSocketInterceptor> _logger;

    /// <summary>Creates the interceptor over the dispatcher's routing table.</summary>
    public TraxJwtDispatcherSocketInterceptor(
        JwtDispatcherRuntime dispatcher,
        IOptionsMonitor<JwtBearerOptions> optionsMonitor,
        TraxApplicationServices applicationServices,
        ILogger<TraxJwtDispatcherSocketInterceptor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(applicationServices);
        ArgumentNullException.ThrowIfNull(logger);
        _dispatcher = dispatcher;
        _applicationServices = applicationServices;
        _logger = logger;
    }

    /// <summary>
    /// Accepts the connection when the <c>connection_init</c> payload carries a JWT that validates
    /// against the scheme its issuer maps to, and rejects it otherwise.
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

        var scheme = _dispatcher.ResolveSchemeForToken(token);
        if (scheme is null)
            return ConnectionStatus.Reject("Token issuer is not recognized.");

        // A scheme AddTraxJwtAuth did not register has no Trax principal resolution in its
        // handler, and the socket has no resolver for it either.
        var registry = _applicationServices.Services.GetService<JwtResolverRegistry>();
        if (registry is null || !registry.SchemeNames.Contains(scheme))
            return ConnectionStatus.Reject(
                $"No principal resolver is registered for scheme '{scheme}'."
            );

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
            _logger.LogWarning(
                ex,
                "Trax dispatcher subscription JWT authentication threw for scheme {Scheme}.",
                scheme
            );
            return ConnectionStatus.Reject("JWT validation failed.");
        }

        if (!result.Succeeded || result.Principal is not { } principal)
            return ConnectionStatus.Reject(JwtSocketAuthentication.RejectedMessage);

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
