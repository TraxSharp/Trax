using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Broadcaster.SignalR.Services;

/// <summary>
/// Bridges Trax's lifecycle event stream onto <see cref="TraxTrainEventHub"/>.
/// Registered as a singleton; exposed in DI as both an <see cref="ITrainLifecycleHook"/>
/// (so local events fire directly without a transport hop) and an
/// <see cref="ITrainEventHandler"/> (so remote events received via the broadcaster
/// transport, e.g. RabbitMQ, also reach connected clients).
/// </summary>
internal sealed class SignalRTrainEventDispatcher : ITrainLifecycleHook, ITrainEventHandler
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    private readonly IHubContext<TraxTrainEventHub, ITraxTrainEventClient> _hub;
    private readonly SignalRSinkConfiguration _config;
    private readonly ILogger<SignalRTrainEventDispatcher>? _logger;

    public SignalRTrainEventDispatcher(
        IHubContext<TraxTrainEventHub, ITraxTrainEventClient> hub,
        SignalRSinkConfiguration config,
        ILogger<SignalRTrainEventDispatcher>? logger = null
    )
    {
        _hub = hub;
        _config = config;
        _logger = logger;
    }

    public Task OnStarted(Metadata metadata, CancellationToken ct) =>
        DispatchAsync(BuildMessage(metadata, "Started"), ct);

    public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
        DispatchAsync(BuildMessage(metadata, "Completed"), ct);

    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        DispatchAsync(BuildMessage(metadata, "Failed"), ct);

    public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
        DispatchAsync(BuildMessage(metadata, "Cancelled"), ct);

    public Task OnStateChanged(Metadata metadata, CancellationToken ct) =>
        DispatchAsync(BuildMessage(metadata, "StateChanged"), ct);

    public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct) =>
        DispatchAsync(message, ct);

    internal async Task DispatchAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (!_config.Matches(message))
            return;

        try
        {
            var payload = _config.Projection(message);
            await _hub.Clients.All.TrainEvent(payload);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "SignalR sink failed to deliver {EventType} for train {TrainName} ({ExternalId}). "
                    + "The broadcaster pipeline continues.",
                message.EventType,
                message.TrainName,
                message.ExternalId
            );
        }
    }

    private static TrainLifecycleEventMessage BuildMessage(Metadata metadata, string eventType) =>
        new(
            MetadataId: metadata.Id,
            ExternalId: metadata.ExternalId,
            TrainName: metadata.Name,
            TrainState: metadata.TrainState.ToString(),
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            FailureJunction: metadata.FailureJunction,
            FailureReason: metadata.FailureReason,
            EventType: eventType,
            Executor: LocalExecutor,
            Output: metadata.Output,
            HostName: metadata.HostName,
            HostEnvironment: metadata.HostEnvironment
        );
}
