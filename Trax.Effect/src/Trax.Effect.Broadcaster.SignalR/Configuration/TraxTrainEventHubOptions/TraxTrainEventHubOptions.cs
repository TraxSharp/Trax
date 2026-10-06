using Microsoft.AspNetCore.Http.Connections;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;

/// <summary>
/// Options for the train-event hub, passed to <c>MapTraxTrainEventHub</c>. The hub pushes every
/// train's lifecycle events to every connected client, so it must be mapped with an authorization
/// posture: <see cref="RequireAuthorization"/>, <see cref="RequireRoles"/>, or an explicit
/// <see cref="AllowAnonymous"/>. Mapping it with none fails at startup.
/// </summary>
public partial class TraxTrainEventHubOptions
{
    internal TraxTrainEventHubOptions() { }

    private readonly List<string> _policies = [];
    private readonly List<string> _roles = [];
    private bool _requireAuthorization;
    private bool _allowAnonymous;
    private Action<HttpConnectionDispatcherOptions>? _configureConnection;
}
