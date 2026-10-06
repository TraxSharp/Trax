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
public sealed class TraxApiKeySocketInterceptor : DefaultSocketSessionInterceptor
{
    private readonly TraxApplicationServices _applicationServices;
    private readonly ILogger<TraxApiKeySocketInterceptor> _logger;
    private readonly TimeSpan _recheckInterval;

    /// <summary>
    /// Creates the interceptor over the application container. An accepted connection's key is
    /// re-resolved every five minutes.
    /// </summary>
    public TraxApiKeySocketInterceptor(
        TraxApplicationServices applicationServices,
        ILogger<TraxApiKeySocketInterceptor> logger
    )
        : this(
            applicationServices,
            logger,
            TraxCompositeSocketInterceptor.DefaultCredentialRecheckInterval
        ) { }

    /// <summary>Creates the interceptor with the re-check interval the GraphQL builder set.</summary>
    internal TraxApiKeySocketInterceptor(
        TraxApplicationServices applicationServices,
        ILogger<TraxApiKeySocketInterceptor> logger,
        TimeSpan recheckInterval
    )
    {
        ArgumentNullException.ThrowIfNull(applicationServices);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(recheckInterval, TimeSpan.Zero);
        _applicationServices = applicationServices;
        _logger = logger;
        _recheckInterval = recheckInterval;
    }

    /// <summary>
    /// Accepts the connection when the <c>connection_init</c> payload carries an API key that the
    /// registered resolver maps to a principal, and rejects it otherwise. An accepted connection's
    /// key is resolved again at every re-check interval, and the connection is closed with
    /// <c>PolicyViolation</c> once it no longer resolves to the same principal (the same id,
    /// type, roles and claims), or the resolver fails.
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
            principal = await ResolveAsync(apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trax subscription API-key resolver threw an exception.");
            return ConnectionStatus.Reject("Auth resolver failed.");
        }

        if (principal is null)
            return ConnectionStatus.Reject("Invalid auth token.");

        // Authentication-type string matches the REST scheme name so downstream
        // code that inspects ClaimsPrincipal.Identity.AuthenticationType sees a
        // consistent identifier across HTTP and WS paths.
        var claimsPrincipal = principal.ToClaimsPrincipal("TraxApiKey");
        AttachPrincipalToRequest(session, claimsPrincipal);

        var status = await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
        if (status.Accepted)
            RecheckKey(session, apiKey, principal);
        return status;
    }

    /// <summary>
    /// Resolves <paramref name="apiKey"/> in a scope of its own. The resolver is scoped: it is the
    /// host's code and typically hits a database. A socket interceptor is a singleton, so it
    /// cannot hold one.
    /// </summary>
    private async ValueTask<TraxPrincipal?> ResolveAsync(
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _applicationServices.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITraxPrincipalResolver<string>>();
        return await resolver.ResolveAsync(apiKey, cancellationToken);
    }

    /// <summary>
    /// Re-resolves the connection's key every the re-check interval, so a revoked or
    /// rotated key ends its open sockets within one interval. See
    /// <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
    /// </summary>
    private void RecheckKey(ISocketSession session, string apiKey, TraxPrincipal connectedAs)
    {
        var services = _applicationServices.Services;
        SocketConnectionLifetime
            .For(
                session.Connection,
                services.GetService<TimeProvider>() ?? TimeProvider.System,
                _logger
            )
            .RecheckEvery(
                _recheckInterval,
                async ct => SamePrincipal(await ResolveAsync(apiKey, ct), connectedAs)
            );
    }

    /// <summary>
    /// True when <paramref name="current"/> grants what <paramref name="connectedAs"/> did: the
    /// same id, type, roles and claims. A display name grants nothing and is not compared.
    /// </summary>
    internal static bool SamePrincipal(TraxPrincipal? current, TraxPrincipal connectedAs) =>
        current is not null
        && current.Id == connectedAs.Id
        && current.PrincipalType == connectedAs.PrincipalType
        && current.Roles.ToHashSet(StringComparer.Ordinal).SetEquals(connectedAs.Roles)
        && SameClaims(current.Claims, connectedAs.Claims);

    private static bool SameClaims(
        IReadOnlyDictionary<string, string>? a,
        IReadOnlyDictionary<string, string>? b
    )
    {
        a ??= new Dictionary<string, string>();
        b ??= new Dictionary<string, string>();
        return a.Count == b.Count
            && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
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
