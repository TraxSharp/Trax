using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// Publishes each step of a run: stores it through the <see cref="IJunctionRunSink"/>, sends it over
/// the <see cref="ITrainEventBroadcaster"/> when the host has one, and hands it to the run's local
/// <see cref="IJunctionEventHandler"/>s. Registered by <c>AddJunctionEvents</c>; its presence is what
/// turns junction events on for a run.
/// </summary>
/// <remarks>
/// Nothing it does can fail a run or change a junction's result. The store only queues; the
/// broadcaster, as shipped, only queues; and every failure, of the store, the transport or a
/// handler, is logged and swallowed.
/// </remarks>
internal sealed class JunctionEventPublisher
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    private readonly IServiceProvider _root;
    private readonly ILogger<JunctionEventPublisher>? _logger;
    private readonly Lazy<ITrainEventBroadcaster?> _broadcaster;
    private readonly Lazy<IJunctionRunSink?> _sink;
    private readonly Lazy<string> _instanceId;

    public JunctionEventPublisher(
        IServiceProvider root,
        ILogger<JunctionEventPublisher>? logger = null
    )
    {
        _root = root;
        _logger = logger;
        _broadcaster = new(() => _root.GetService<ITrainEventBroadcaster>());
        _sink = new(() => _root.GetService<IJunctionRunSink>());
        _instanceId = new(() =>
            (_root.GetService<BroadcastInstance>() ?? BroadcastInstance.Unregistered).Id
        );
    }

    /// <summary>
    /// The longest a run waits, as it begins, for its attempt to be worked out. Past it the run
    /// carries on and its events carry no attempt.
    /// </summary>
    internal TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Begins the junction events of the run <paramref name="metadata"/> records, working out once
    /// which attempt of its manifest it is. Null for a run that was never persisted, which has no
    /// junction events. Never throws: an attempt that cannot be worked out is
    /// logged and left out.
    /// </summary>
    public async Task<JunctionEventRun?> BeginAsync(
        Metadata metadata,
        Type train,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        // A run that was never persisted has no row its steps could be read back or told apart by,
        // so it publishes and records none.
        if (metadata.Id <= 0)
            return null;

        int? attempt = null;

        if (metadata.ManifestId is not null)
        {
            try
            {
                if (_root.GetService<IRunAttempts>() is { } attempts)
                {
                    // The run waits on this, so it is bounded: a busy pool or a slow database
                    // costs the run at most AttemptTimeout, and its events carry no attempt.
                    using var bound = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken
                    );
                    bound.CancelAfter(AttemptTimeout);
                    attempt = await attempts
                        .AttemptOf(metadata, bound.Token)
                        .WaitAsync(AttemptTimeout, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger?.LogWarning(
                    e,
                    "Could not work out which attempt of manifest {ManifestId} run {ExternalId} is; "
                        + "its junction events carry no attempt, and the run carries on.",
                    metadata.ManifestId,
                    metadata.ExternalId
                );
            }
        }

        return new(this, metadata, train, services, attempt);
    }

    /// <summary>Stores, broadcasts and hands out one step. Never throws.</summary>
    public async Task Publish(JunctionEventRun run, string eventType, JunctionEventPayload step)
    {
        step = step with { Attempt = run.Attempt };
        var metadata = run.Metadata;

        try
        {
            _sink.Value?.Write(metadata.Id, step);
        }
        catch (Exception e)
        {
            _logger?.LogWarning(
                e,
                "Could not store step {Position} ({Name}) of run {ExternalId}; the run carries on.",
                step.Position,
                step.Name,
                run.ExternalId
            );
        }

        var message = new TrainLifecycleEventMessage(
            MetadataId: metadata.Id,
            ExternalId: metadata.ExternalId,
            TrainName: metadata.Name,
            TrainState: TrainState.InProgress.ToString(),
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: LocalExecutor,
            Output: null,
            HostName: metadata.HostName,
            HostEnvironment: metadata.HostEnvironment
        )
        {
            InstanceId = _instanceId.Value,
            Junction = step,
        };

        try
        {
            if (_broadcaster.Value is { } broadcaster)
                await broadcaster.PublishAsync(message, CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger?.LogWarning(
                e,
                "Could not broadcast {EventType} for step {Position} ({Name}) of run {ExternalId}; the run carries on.",
                eventType,
                step.Position,
                step.Name,
                run.ExternalId
            );
        }

        IEnumerable<IJunctionEventHandler> handlers;
        try
        {
            handlers = run.Services.GetServices<IJunctionEventHandler>().ToList();
        }
        catch (Exception e)
        {
            _logger?.LogWarning(
                e,
                "Could not resolve the junction event handlers for run {ExternalId}; the run carries on.",
                run.ExternalId
            );
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(message, CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger?.LogWarning(
                    e,
                    "JunctionEventHandler ({HandlerType}) threw while handling {EventType} for run {ExternalId}; the run carries on.",
                    handler.GetType().Name,
                    eventType,
                    run.ExternalId
                );
            }
        }
    }
}
