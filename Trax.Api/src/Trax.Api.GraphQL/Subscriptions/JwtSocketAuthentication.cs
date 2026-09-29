using System.Runtime.CompilerServices;
using System.Security.Claims;
using HotChocolate.AspNetCore.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Authenticates a JWT carried in a subscription's <c>connection_init</c> payload by running the
/// scheme's own authentication handler, exactly as for an HTTP request that carries the token as
/// <c>Authorization: Bearer</c>. Shared by every JWT socket interceptor.
/// </summary>
/// <remarks>
/// Running the handler, rather than repeating its validation, is what keeps the two transports
/// from diverging: the host's <c>JwtBearerEvents</c> (<c>OnMessageReceived</c>,
/// <c>OnTokenValidated</c>, <c>OnAuthenticationFailed</c>), the JWKS refresh on an unknown key id,
/// Trax's own principal resolution in <c>OnTokenValidated</c>, and any
/// <see cref="IClaimsTransformation"/> all run on the socket as they do on HTTP.
/// <para>
/// The handler runs against a request built from the upgrade request (scheme, host, path, query,
/// headers and connection addresses), with the payload token as its bearer header, inside a scope
/// of its own, so nothing the upgrade request's own authentication cached is reused.
/// </para>
/// <para>
/// A connection outlives the moment its token was checked, so
/// <see cref="CloseAtExpiry"/> closes it when the token expires. See
/// <c>docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md</c>.
/// </para>
/// </remarks>
internal static class JwtSocketAuthentication
{
    /// <summary>The close message a session receives when its token expires.</summary>
    internal const string ExpiredMessage = "The access token has expired.";

    // Timers stay referenced for as long as their connection is, and no longer.
    private static readonly ConditionalWeakTable<ISocketConnection, ITimer> ExpiryTimers = new();

    // ITimer's largest due time, a little under 50 days. A later expiry is reached in steps.
    private static readonly TimeSpan MaxTimerDue = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Authenticates <paramref name="token"/> with <paramref name="scheme"/>'s handler, in a
    /// request built from <paramref name="upgrade"/>. <paramref name="services"/> must be a scope
    /// owned by the caller; the handler and the host's events resolve their services from it.
    /// </summary>
    public static Task<AuthenticateResult> AuthenticateAsync(
        IServiceProvider services,
        HttpContext? upgrade,
        string scheme,
        string token,
        CancellationToken cancellationToken
    )
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = cancellationToken,
        };

        if (upgrade is not null)
        {
            var from = upgrade.Request;
            var to = context.Request;
            to.Scheme = from.Scheme;
            to.Host = from.Host;
            to.PathBase = from.PathBase;
            to.Path = from.Path;
            to.QueryString = from.QueryString;
            foreach (var header in from.Headers)
                to.Headers[header.Key] = header.Value;
            context.Connection.RemoteIpAddress = upgrade.Connection.RemoteIpAddress;
            context.Connection.RemotePort = upgrade.Connection.RemotePort;
            context.Connection.LocalIpAddress = upgrade.Connection.LocalIpAddress;
            context.Connection.LocalPort = upgrade.Connection.LocalPort;
        }

        context.Request.Headers.Authorization = "Bearer " + token;

        return services
            .GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, scheme);
    }

    /// <summary>
    /// What a connection the handler refused is told: "Invalid JWT.", whatever the reason (an
    /// invalid token, a JWKS that could not be fetched, a host event or resolver that refused).
    /// The handler's own message is not passed on, as HTTP does not return it either.
    /// </summary>
    public const string RejectedMessage = "Invalid JWT.";

    /// <summary>
    /// When the credential expires: the handler's <c>ExpiresUtc</c>, which it takes from the token,
    /// or the token's own <c>exp</c>. <c>null</c> when neither is known.
    /// </summary>
    public static DateTimeOffset? ExpiresAt(AuthenticateResult result, string token)
    {
        if (result.Properties?.ExpiresUtc is { } expires)
            return expires;

        try
        {
            var validTo = new JsonWebToken(token).ValidTo;
            return validTo == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(validTo, DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Closes <paramref name="session"/>'s connection with <c>PolicyViolation</c> at
    /// <paramref name="expiresAt"/>. A later HTTP request with the same token would be refused
    /// from then on; the connection is refused the same way. The timer goes with the connection.
    /// </summary>
    public static void CloseAtExpiry(
        ISocketSession session,
        DateTimeOffset? expiresAt,
        IServiceProvider applicationServices,
        ILogger logger
    )
    {
        if (expiresAt is not { } at)
            return;

        var connection = session.Connection;
        var time = applicationServices.GetService<TimeProvider>() ?? TimeProvider.System;

        ITimer? timer = null;
        timer = time.CreateTimer(
            _ =>
            {
                var remaining = at - time.GetUtcNow();
                if (remaining > TimeSpan.Zero)
                {
                    timer?.Change(Min(remaining, MaxTimerDue), Timeout.InfiniteTimeSpan);
                    return;
                }

                _ = CloseAsync(connection, logger);
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );

        ExpiryTimers.AddOrUpdate(connection, timer);
        connection.RequestAborted.Register(static state => ((ITimer)state!).Dispose(), timer);

        var due = at - time.GetUtcNow();
        timer.Change(
            due <= TimeSpan.Zero ? TimeSpan.Zero : Min(due, MaxTimerDue),
            Timeout.InfiniteTimeSpan
        );
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static async Task CloseAsync(ISocketConnection connection, ILogger logger)
    {
        try
        {
            await connection
                .CloseAsync(ExpiredMessage, ConnectionCloseReason.PolicyViolation, default)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Closing a subscription connection whose token expired failed.");
        }
        finally
        {
            if (ExpiryTimers.TryGetValue(connection, out var timer))
            {
                ExpiryTimers.Remove(connection);
                timer.Dispose();
            }
        }
    }

    /// <summary>Sets the upgrade request's user to <paramref name="principal"/>.</summary>
    public static void AttachPrincipal(ISocketSession session, ClaimsPrincipal principal)
    {
        if (session.Connection.HttpContext is { } httpContext)
            httpContext.User = principal;
    }
}
