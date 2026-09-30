using Microsoft.AspNetCore.Http.Connections;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;

public partial class TraxTrainEventHubOptions
{
    /// <summary>
    /// Admits only callers the host's authorization accepts. With no arguments any authenticated
    /// caller is admitted (the host's default policy); with policy names, a caller must satisfy
    /// every one. Calling it again adds policies. Combines with <see cref="RequireRoles"/>, which
    /// then also applies.
    /// </summary>
    /// <param name="policies">Authorization policy names registered on the host.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentException">A policy name is null or whitespace.</exception>
    public TraxTrainEventHubOptions RequireAuthorization(params string[] policies)
    {
        foreach (var policy in policies ?? [])
        {
            if (string.IsNullOrWhiteSpace(policy))
            {
                throw new ArgumentException(
                    "RequireAuthorization() does not accept null or whitespace policy names.",
                    nameof(policies)
                );
            }

            _policies.Add(policy);
        }

        _requireAuthorization = true;
        return this;
    }

    /// <summary>
    /// Admits only authenticated callers in at least one of <paramref name="roles"/>. Calling it
    /// again adds roles. Combines with <see cref="RequireAuthorization"/>, which then also applies.
    /// </summary>
    /// <param name="roles">Role names; at least one is required.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentException">No role is given, or one is null or whitespace.</exception>
    public TraxTrainEventHubOptions RequireRoles(params string[] roles)
    {
        if (roles is null || roles.Length == 0)
        {
            throw new ArgumentException(
                "RequireRoles() requires at least one role.",
                nameof(roles)
            );
        }

        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException(
                    "RequireRoles() does not accept null or whitespace role names.",
                    nameof(roles)
                );
            }

            _roles.Add(role);
        }

        return this;
    }

    /// <summary>
    /// Opens the hub to any client that can reach it, overriding any fallback policy on the host.
    /// Every such client receives every train's lifecycle events (train name, external id, event
    /// type, timestamp and whatever the sink's projection adds), so this suits a hub reachable
    /// only from a trusted network. A warning is logged at startup when it is used. Cannot be
    /// combined with <see cref="RequireAuthorization"/> or <see cref="RequireRoles"/>.
    /// </summary>
    /// <returns>The same options, for chaining.</returns>
    public TraxTrainEventHubOptions AllowAnonymous()
    {
        _allowAnonymous = true;
        return this;
    }

    /// <summary>
    /// Adjusts the hub's connection options after Trax has applied its defaults, so a value set
    /// here wins. Trax sets <see cref="HttpConnectionDispatcherOptions.TransportSendTimeout"/> to
    /// <c>SignalRHubEndpointExtensions.DefaultTransportSendTimeout</c>.
    /// </summary>
    /// <param name="configure">Runs against the hub's connection options.</param>
    /// <returns>The same options, for chaining.</returns>
    public TraxTrainEventHubOptions ConfigureConnection(
        Action<HttpConnectionDispatcherOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureConnection += configure;
        return this;
    }
}
