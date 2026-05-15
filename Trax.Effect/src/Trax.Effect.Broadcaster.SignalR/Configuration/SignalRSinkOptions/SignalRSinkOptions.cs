using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;

/// <summary>
/// Fluent options for the SignalR broadcaster sink.
/// Use inside <c>UseSignalRHub(opts =&gt; ...)</c> to filter events
/// and customize the payload sent to clients.
/// </summary>
public partial class SignalRSinkOptions
{
    internal SignalRSinkOptions() { }

    private readonly HashSet<string> _eventTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _trainNames = new(StringComparer.Ordinal);
    private Func<TrainLifecycleEventMessage, object> _projection =
        DefaultTraxClientEventProjection.Project;
}
