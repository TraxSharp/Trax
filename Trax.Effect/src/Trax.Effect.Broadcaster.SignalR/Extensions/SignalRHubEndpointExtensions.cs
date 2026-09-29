using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.SignalR.Services;

namespace Trax.Effect.Broadcaster.SignalR.Extensions;

/// <summary>
/// Adds <c>MapTraxTrainEventHub</c>, which maps the SignalR hub that clients connect to in order to
/// receive the events <c>UseSignalRHub</c> pushes.
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
    /// Maps the Trax train-event hub at <paramref name="path"/>. Clients connect here to
    /// receive train lifecycle events pushed by <c>UseSignalRHub()</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint builder (typically <c>WebApplication</c>).</param>
    /// <param name="path">URL path where clients open the SignalR connection.</param>
    /// <remarks>
    /// The hub's <see cref="HttpConnectionDispatcherOptions.TransportSendTimeout"/> is set to
    /// <see cref="DefaultTransportSendTimeout"/>. Use the overload that takes
    /// <c>configureOptions</c> to change it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>services.AddSignalR()</c> has not been called on the host. Add it
    /// before <c>builder.Build()</c>.
    /// </exception>
    public static HubEndpointConventionBuilder MapTraxTrainEventHub(
        this IEndpointRouteBuilder endpoints,
        string path = "/hubs/trax-events"
    ) => endpoints.MapTraxTrainEventHub(path, _ => { });

    /// <summary>
    /// Maps the Trax train-event hub at <paramref name="path"/>, and lets the host adjust the
    /// connection options after Trax has applied its defaults.
    /// </summary>
    /// <param name="endpoints">The endpoint builder (typically <c>WebApplication</c>).</param>
    /// <param name="path">URL path where clients open the SignalR connection.</param>
    /// <param name="configureOptions">
    /// Runs after <see cref="HttpConnectionDispatcherOptions.TransportSendTimeout"/> is set to
    /// <see cref="DefaultTransportSendTimeout"/>, so a value set here wins.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>services.AddSignalR()</c> has not been called on the host. Add it
    /// before <c>builder.Build()</c>.
    /// </exception>
    public static HubEndpointConventionBuilder MapTraxTrainEventHub(
        this IEndpointRouteBuilder endpoints,
        string path,
        Action<HttpConnectionDispatcherOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

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
                    + "    app.MapTraxTrainEventHub();"
            );
        }

        return endpoints.MapHub<TraxTrainEventHub>(
            path,
            options =>
            {
                options.TransportSendTimeout = DefaultTransportSendTimeout;
                configureOptions(options);
            }
        );
    }
}
