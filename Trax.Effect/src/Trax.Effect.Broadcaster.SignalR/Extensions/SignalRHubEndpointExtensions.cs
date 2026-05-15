using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.SignalR.Services;

namespace Trax.Effect.Broadcaster.SignalR.Extensions;

public static class SignalRHubEndpointExtensions
{
    /// <summary>
    /// Maps the Trax train-event hub at <paramref name="path"/>. Clients connect here to
    /// receive train lifecycle events pushed by <c>UseSignalRHub()</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint builder (typically <c>WebApplication</c>).</param>
    /// <param name="path">URL path where clients open the SignalR connection.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>services.AddSignalR()</c> has not been called on the host. Add it
    /// before <c>builder.Build()</c>.
    /// </exception>
    public static HubEndpointConventionBuilder MapTraxTrainEventHub(
        this IEndpointRouteBuilder endpoints,
        string path = "/hubs/trax-events"
    )
    {
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

        return endpoints.MapHub<TraxTrainEventHub>(path);
    }
}
