using Microsoft.AspNetCore.Http.Connections;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;

public partial class TraxTrainEventHubOptions
{
    /// <summary>
    /// Validates the posture and produces what <c>MapTraxTrainEventHub</c> applies to the endpoint.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No posture was chosen, or <see cref="AllowAnonymous"/> was combined with a requirement.
    /// </exception>
    internal TrainEventHubPosture Build()
    {
        var requires = _requireAuthorization || _roles.Count > 0;

        if (_allowAnonymous && requires)
        {
            throw new InvalidOperationException(
                "MapTraxTrainEventHub() was given AllowAnonymous() together with "
                    + "RequireAuthorization() or RequireRoles(). They contradict each other: choose one."
            );
        }

        if (!_allowAnonymous && !requires)
        {
            throw new InvalidOperationException(
                "MapTraxTrainEventHub() requires an authorization posture, because every connected client "
                    + "receives every train's lifecycle events. Choose one, e.g.:\n"
                    + "    app.MapTraxTrainEventHub(hub => hub.RequireAuthorization(\"TraxEvents\"));\n"
                    + "    app.MapTraxTrainEventHub(hub => hub.RequireRoles(\"Operator\"));\n"
                    + "    app.MapTraxTrainEventHub(hub => hub.AllowAnonymous()); // trusted networks only"
            );
        }

        return new TrainEventHubPosture(
            _allowAnonymous,
            _requireAuthorization,
            _policies.ToArray(),
            _roles.ToArray(),
            _configureConnection ?? (_ => { })
        );
    }
}

/// <summary>The validated authorization posture and connection options for the hub endpoint.</summary>
internal sealed record TrainEventHubPosture(
    bool AllowAnonymous,
    bool RequireAuthorization,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Roles,
    Action<HttpConnectionDispatcherOptions> ConfigureConnection
);
