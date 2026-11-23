using Microsoft.AspNetCore.Http.Connections;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;

/// <summary>
/// The validated authorization posture, connection options and allowed origins for the hub
/// endpoint. <see cref="AllowedOrigins"/> is null when the host's CORS default policy decides.
/// </summary>
internal sealed record TrainEventHubPosture(
    bool AllowAnonymous,
    bool RequireAuthorization,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Roles,
    Action<HttpConnectionDispatcherOptions> ConfigureConnection,
    IReadOnlyList<string>? AllowedOrigins = null
);
