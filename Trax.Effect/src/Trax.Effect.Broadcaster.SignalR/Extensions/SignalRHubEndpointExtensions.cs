using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;
using Trax.Effect.Broadcaster.SignalR.Services;

namespace Trax.Effect.Broadcaster.SignalR.Extensions;

/// <summary>
/// Adds <c>MapTraxTrainEventHub</c>, which maps the SignalR hub that clients connect to in order to
/// receive the events <c>UseSignalRHub</c> pushes. The hub carries the host's authorization: it is
/// mapped with an explicit posture or not at all.
/// </summary>
public static class SignalRHubEndpointExtensions
{
    /// <summary>
    /// The <see cref="HttpConnectionDispatcherOptions.TransportSendTimeout"/> the hub is mapped
    /// with, unless the host overrides it. ASP.NET Core's own default is 10 seconds. A client that
    /// cannot take a send within this time is disconnected, so it holds up the sink's background
    /// sender for no longer than this.
    /// </summary>
    public static readonly TimeSpan DefaultTransportSendTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maps the Trax train-event hub at <c>/hubs/trax-events</c>. See
    /// <see cref="MapTraxTrainEventHub(IEndpointRouteBuilder, string, Action{TraxTrainEventHubOptions})"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint builder (typically <c>WebApplication</c>).</param>
    /// <param name="configure">Chooses the hub's authorization posture, and optionally its connection options.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="configure"/> chose no authorization posture or named a policy the host has
    /// not registered, or <c>services.AddSignalR()</c> has not been called on the host.
    /// </exception>
    public static HubEndpointConventionBuilder MapTraxTrainEventHub(
        this IEndpointRouteBuilder endpoints,
        Action<TraxTrainEventHubOptions> configure
    ) => endpoints.MapTraxTrainEventHub(DefaultPath, configure);

    /// <summary>
    /// Maps the Trax train-event hub at <paramref name="path"/>. Clients connect here to receive
    /// train lifecycle events pushed by <c>UseSignalRHub()</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint builder (typically <c>WebApplication</c>).</param>
    /// <param name="path">URL path where clients open the SignalR connection.</param>
    /// <param name="configure">
    /// Chooses the hub's authorization posture: <c>RequireAuthorization(...)</c>,
    /// <c>RequireRoles(...)</c>, or an explicit <c>AllowAnonymous()</c>. Every connected client
    /// receives every train's events, so there is no default.
    /// </param>
    /// <remarks>
    /// The posture is applied to the hub's endpoints, so the host's authentication and
    /// authorization middleware (<c>UseAuthentication()</c>, <c>UseAuthorization()</c>) decide who
    /// may connect. Every policy the posture names is resolved through the host's
    /// <see cref="IAuthorizationPolicyProvider"/> here, so an unknown one fails at startup. A bare
    /// <c>RequireAuthorization()</c> applies the host's default policy and, when it has one, its
    /// fallback policy as well. A connection is closed once the authentication it was admitted on
    /// expires (<see cref="HttpConnectionDispatcherOptions.CloseOnAuthenticationExpiration"/>,
    /// which <c>ConfigureConnection</c> cannot turn off). <c>AllowAnonymous()</c> is logged as a
    /// warning at startup. The hub's
    /// <see cref="HttpConnectionDispatcherOptions.TransportSendTimeout"/> is set to
    /// <see cref="DefaultTransportSendTimeout"/>; change it with <c>ConfigureConnection</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="configure"/> chose no authorization posture, or combined
    /// <c>AllowAnonymous()</c> with a requirement, or named a policy the host has not registered;
    /// or <c>services.AddSignalR()</c> has not been called on the host.
    /// </exception>
    public static HubEndpointConventionBuilder MapTraxTrainEventHub(
        this IEndpointRouteBuilder endpoints,
        string path,
        Action<TraxTrainEventHubOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (
            endpoints.ServiceProvider.GetService<
                IHubContext<TraxTrainEventHub, ITraxTrainEventClient>
            >()
            is null
        )
        {
            throw new InvalidOperationException(
                "MapTraxTrainEventHub() requires SignalR services. "
                    + "Call services.AddSignalR() on the host before building the application, e.g.:\n"
                    + "    builder.Services.AddSignalR();\n"
                    + "    builder.Services.AddTrax(t => t.AddEffects(e => e.UseBroadcaster(b => b.UseSignalRHub())));\n"
                    + "    var app = builder.Build();\n"
                    + "    app.MapTraxTrainEventHub(hub => hub.RequireAuthorization());"
            );
        }

        var options = new TraxTrainEventHubOptions();
        configure(options);
        var posture = options.Build();

        // Resolve every policy the posture names now, so a misspelled name or a host without
        // authorization fails where the hub is mapped rather than at the first connection.
        AuthorizationPolicy? fallback = null;
        if (!posture.AllowAnonymous)
            fallback = ResolvePolicies(endpoints.ServiceProvider, posture, path);

        var hub = endpoints.MapHub<TraxTrainEventHub>(
            path,
            connection =>
            {
                connection.TransportSendTimeout = DefaultTransportSendTimeout;
                posture.ConfigureConnection(connection);
                // Authorization runs when a connection opens; this closes it once the
                // authentication it was admitted on expires. Set after the host's own options
                // so it cannot be turned off.
                connection.CloseOnAuthenticationExpiration = true;
            }
        );

        if (posture.AllowAnonymous)
        {
            hub.AllowAnonymous();
            endpoints
                .ServiceProvider.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(SignalRHubEndpointExtensions).FullName!)
                .LogWarning(
                    "The Trax train-event hub at {Path} is mapped with AllowAnonymous(): any client that "
                        + "can reach it receives every train's lifecycle events.",
                    path
                );
            return hub;
        }

        if (posture.RequireAuthorization)
        {
            if (posture.Policies.Count == 0)
            {
                // Any authorization metadata on an endpoint switches the host's fallback policy
                // off, so a bare requirement would evaluate only the default policy. Keep the
                // fallback as well: the hub is never more open than an unannotated endpoint.
                hub.RequireAuthorization();
                if (fallback is not null)
                    hub.RequireAuthorization(fallback);
            }
            else
                hub.RequireAuthorization(posture.Policies.ToArray());
        }

        if (posture.Roles.Count > 0)
            hub.RequireAuthorization(
                new AuthorizeAttribute { Roles = string.Join(',', posture.Roles) }
            );

        return hub;
    }

    /// <summary>
    /// Resolves each policy <paramref name="posture"/> names through the host's
    /// <see cref="IAuthorizationPolicyProvider"/> and refuses an unknown one. Returns the host's
    /// fallback policy, or <c>null</c> when it has none.
    /// </summary>
    private static AuthorizationPolicy? ResolvePolicies(
        IServiceProvider services,
        TrainEventHubPosture posture,
        string path
    )
    {
        var provider =
            services.GetService<IAuthorizationPolicyProvider>()
            ?? throw new InvalidOperationException(
                $"MapTraxTrainEventHub() at {path} requires authorization, but the host has no "
                    + "IAuthorizationPolicyProvider. Call builder.Services.AddAuthorization() before building the application."
            );

        foreach (var name in posture.Policies)
        {
            if (provider.GetPolicyAsync(name).GetAwaiter().GetResult() is null)
            {
                throw new InvalidOperationException(
                    $"MapTraxTrainEventHub() at {path} requires the authorization policy '{name}', "
                        + "which the host has not registered. Register it, e.g.:\n"
                        + $"    builder.Services.AddAuthorization(o => o.AddPolicy(\"{name}\", p => ...));"
                );
            }
        }

        return provider.GetFallbackPolicyAsync().GetAwaiter().GetResult();
    }

    private const string DefaultPath = "/hubs/trax-events";
}
