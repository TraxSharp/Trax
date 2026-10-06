using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Authenticates a subscription JWT against every scheme registered with <c>AddTraxJwtAuth</c>,
/// named or default, when no <c>AddTraxJwtDispatcher</c> routes between them.
/// </summary>
/// <remarks>
/// Each scheme's own handler authenticates the token, in registration order, as it would an HTTP
/// request carrying it (see <see cref="JwtSocketAuthentication"/>); the first scheme that succeeds
/// gives the connection its principal, which that scheme's <c>OnTokenValidated</c> resolved. A
/// token no scheme accepts is rejected, and the connection closes when the token expires. See
/// <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c> and
/// <c>docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md</c>.
/// </remarks>
internal sealed class TraxJwtSchemesSocketInterceptor(
    JwtResolverRegistry registry,
    TraxApplicationServices applicationServices,
    ILogger<TraxJwtSchemesSocketInterceptor> logger
) : DefaultSocketSessionInterceptor
{
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var payload = ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(
            connectionInitMessage
        );
        var token = payload?.AuthToken ?? payload?.Bearer;
        if (string.IsNullOrWhiteSpace(token))
            return ConnectionStatus.Reject("Missing auth token in connection_init payload.");

        var rejection = JwtSocketAuthentication.RejectedMessage;
        await using (var scope = applicationServices.Services.CreateAsyncScope())
        {
            foreach (var scheme in registry.SchemeNames)
            {
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
                    logger.LogWarning(
                        ex,
                        "Subscription JWT authentication threw for scheme {Scheme}.",
                        scheme
                    );
                    rejection = "JWT validation failed.";
                    continue;
                }

                if (!result.Succeeded || result.Principal is not { } principal)
                    continue;

                JwtSocketAuthentication.AttachPrincipal(session, principal);
                JwtSocketAuthentication.CloseAtExpiry(
                    session,
                    JwtSocketAuthentication.ExpiresAt(result, token),
                    applicationServices.Services,
                    logger
                );
                return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
            }
        }

        return ConnectionStatus.Reject(rejection);
    }

    internal sealed record ConnectionInitPayload(string? AuthToken, string? Bearer);
}
