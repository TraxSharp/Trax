using Microsoft.AspNetCore.SignalR;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// SignalR hub clients connect to in order to receive train lifecycle events.
/// Mapped via <c>endpoints.MapTraxTrainEventHub(hub =&gt; hub.RequireAuthorization(...))</c>, which
/// requires the host to choose who may connect.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/i-trax-train-event-client">SDK reference</seealso>
public sealed class TraxTrainEventHub : Hub<ITraxTrainEventClient> { }
