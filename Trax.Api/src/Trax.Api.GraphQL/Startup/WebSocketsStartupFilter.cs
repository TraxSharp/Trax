using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Prepends <c>UseWebSockets()</c> to the front of the application pipeline so
/// the GraphQL subscription endpoint can always upgrade, regardless of where the
/// host places <c>UseTraxGraphQL()</c> relative to other endpoint middleware.
/// </summary>
/// <remarks>
/// Without this, <c>UseTraxGraphQL()</c> would have to add the WebSockets
/// middleware itself, immediately before mapping the endpoint. That is fragile:
/// any terminal endpoint execution wired earlier in the host pipeline (Blazor's
/// <c>MapRazorComponents</c> via <c>UseTraxDashboard()</c>, an explicit
/// <c>UseEndpoints</c>, etc.) runs the GraphQL endpoint before the upgrade
/// middleware, and the handshake is served as a plain HTTP response instead of
/// a WebSocket upgrade. A startup filter runs ahead of the host's own
/// <c>Configure</c>, so the upgrade middleware is guaranteed to precede all
/// endpoint execution.
/// </remarks>
internal sealed class WebSocketsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.UseWebSockets();
            next(app);
        };
}
