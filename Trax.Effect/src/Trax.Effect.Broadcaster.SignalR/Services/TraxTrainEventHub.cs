using Microsoft.AspNetCore.SignalR;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// SignalR hub clients connect to in order to receive train lifecycle events.
/// Mapped via <c>endpoints.MapTraxTrainEventHub("/hubs/trax-events")</c>.
/// </summary>
public sealed class TraxTrainEventHub : Hub<ITraxTrainEventClient> { }
