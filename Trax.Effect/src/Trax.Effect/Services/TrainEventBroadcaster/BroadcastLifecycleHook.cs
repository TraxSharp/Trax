using System.Reflection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Lifecycle hook that publishes train state transitions to an <see cref="ITrainEventBroadcaster"/>
/// for cross-process delivery. Registered automatically by <c>UseBroadcaster()</c>.
/// </summary>
public class BroadcastLifecycleHook : ITrainLifecycleHook
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    private readonly ITrainEventBroadcaster _broadcaster;
    private readonly ILogger<BroadcastLifecycleHook>? _logger;

    public BroadcastLifecycleHook(
        ITrainEventBroadcaster broadcaster,
        ILogger<BroadcastLifecycleHook>? logger = null
    )
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task OnStarted(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Started", ct);
    }

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Completed", ct);
    }

    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        await PublishAsync(metadata, "Failed", ct);
    }

    public async Task OnCancelled(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Cancelled", ct);
    }

    public async Task OnStateChanged(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "StateChanged", ct);
    }

    private async Task PublishAsync(Metadata metadata, string eventType, CancellationToken ct)
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: metadata.Id,
            ExternalId: metadata.ExternalId,
            TrainName: metadata.Name,
            TrainState: metadata.TrainState.ToString(),
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            FailureStep: metadata.FailureStep,
            FailureReason: metadata.FailureReason,
            EventType: eventType,
            Executor: LocalExecutor,
            Output: metadata.Output,
            HostName: metadata.HostName,
            HostEnvironment: metadata.HostEnvironment
        );

        _logger?.LogDebug(
            "Broadcasting lifecycle event {EventType} for train {TrainName} ({ExternalId}).",
            eventType,
            metadata.Name,
            metadata.ExternalId
        );

        await _broadcaster.PublishAsync(message, ct);
    }
}
