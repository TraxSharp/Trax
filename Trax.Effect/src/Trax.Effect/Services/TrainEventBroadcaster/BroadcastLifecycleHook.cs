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
internal class BroadcastLifecycleHook : ITrainLifecycleHook
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    private readonly ITrainEventBroadcaster _broadcaster;
    private readonly ILogger<BroadcastLifecycleHook>? _logger;

    /// <summary>
    /// Creates the hook. Registered by <c>UseBroadcaster()</c>; not intended to be constructed directly.
    /// </summary>
    /// <param name="broadcaster">The transport each event is published through.</param>
    /// <param name="logger">Optional debug logging of each published event.</param>
    public BroadcastLifecycleHook(
        ITrainEventBroadcaster broadcaster,
        ILogger<BroadcastLifecycleHook>? logger = null
    )
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Publishes a <see cref="TrainLifecycleEventMessage"/> with <c>EventType</c> <c>"Started"</c>, built from the
    /// row's id, external id, name, state, failure, output and host, and stamped with this process's executor.
    /// A publish failure propagates to the lifecycle hook runner, which logs it.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed to <see cref="ITrainEventBroadcaster.PublishAsync"/>.</param>
    public async Task OnStarted(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Started", ct);
    }

    /// <summary>
    /// Publishes a <see cref="TrainLifecycleEventMessage"/> with <c>EventType</c> <c>"Completed"</c>, built from the
    /// row's id, external id, name, state, failure, output and host, and stamped with this process's executor.
    /// A publish failure propagates to the lifecycle hook runner, which logs it.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed to <see cref="ITrainEventBroadcaster.PublishAsync"/>.</param>
    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Completed", ct);
    }

    /// <summary>
    /// Publishes a <see cref="TrainLifecycleEventMessage"/> with <c>EventType</c> <c>"Failed"</c>, built from the
    /// row's id, external id, name, state, failure, output and host, and stamped with this process's executor.
    /// A publish failure propagates to the lifecycle hook runner, which logs it.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="exception">Not published; the message carries the row's <c>FailureReason</c> and
    /// <c>FailureJunction</c> instead.</param>
    /// <param name="ct">Passed to <see cref="ITrainEventBroadcaster.PublishAsync"/>.</param>
    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        await PublishAsync(metadata, "Failed", ct);
    }

    /// <summary>
    /// Publishes a <see cref="TrainLifecycleEventMessage"/> with <c>EventType</c> <c>"Cancelled"</c>, built from the
    /// row's id, external id, name, state, failure, output and host, and stamped with this process's executor.
    /// A publish failure propagates to the lifecycle hook runner, which logs it.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed to <see cref="ITrainEventBroadcaster.PublishAsync"/>.</param>
    public async Task OnCancelled(Metadata metadata, CancellationToken ct)
    {
        await PublishAsync(metadata, "Cancelled", ct);
    }

    /// <summary>
    /// Publishes a <see cref="TrainLifecycleEventMessage"/> with <c>EventType</c> <c>"StateChanged"</c>, built from the
    /// row's id, external id, name, state, failure, output and host, and stamped with this process's executor. The runner calls this after each of the other four, so every transition is published twice: once under its own event type and once as <c>StateChanged</c>.
    /// A publish failure propagates to the lifecycle hook runner, which logs it.
    /// </summary>
    /// <param name="metadata">The run's metadata row.</param>
    /// <param name="ct">Passed to <see cref="ITrainEventBroadcaster.PublishAsync"/>.</param>
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
            FailureJunction: metadata.FailureJunction,
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
