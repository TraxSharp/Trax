using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
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
/// transport, e.g. RabbitMQ, also reach connected clients). With <c>WithJunctionEvents()</c> it is
/// also an <see cref="IJunctionEventHandler"/>, and sends each step of a run through the
/// <c>"JunctionEvent"</c> client method; without it, a junction event is never sent.
/// </summary>
/// <remarks>
/// A train awaits its lifecycle hooks inline, so the hook never waits on delivery to clients.
/// It filters the event and writes it to a bounded queue. One background sender drains the
/// queue in order and calls <c>Clients.All</c>. When the queue is full the event is dropped,
/// counted and logged, and the train carries on. The host's shutdown drains what is queued.
/// </remarks>
internal sealed class SignalRTrainEventDispatcher
    : ITrainLifecycleHook,
        ITrainEventHandler,
        IJunctionEventHandler,
        IHostedService,
        IAsyncDisposable,
        IDisposable
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    /// <summary>
    /// How long <see cref="DisposeAsync"/> waits for the queue to drain when the host did not
    /// stop the dispatcher first.
    /// </summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly IHubContext<TraxTrainEventHub, ITraxTrainEventClient> _hub;
    private readonly SignalRSinkConfiguration _config;
    private readonly ILogger<SignalRTrainEventDispatcher>? _logger;
    private readonly DeliveryQueue _queue;
    private readonly CancellationTokenSource _abandon = new();
    private readonly Task _sender;

    private long _dropped;
    private long _droppedSinceReport;
    private int _stopped;

    public SignalRTrainEventDispatcher(
        IHubContext<TraxTrainEventHub, ITraxTrainEventClient> hub,
        SignalRSinkConfiguration config,
        ILogger<SignalRTrainEventDispatcher>? logger = null
    )
    {
        _hub = hub;
        _config = config;
        _logger = logger;
        _queue = new DeliveryQueue(config.DeliveryQueueCapacity);
        _sender = Task.Run(SendLoopAsync);
    }

    /// <summary>
    /// Events dropped because the delivery queue was full, since the dispatcher was created.
    /// </summary>
    internal long DroppedEvents => Interlocked.Read(ref _dropped);

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

    /// <summary>
    /// Queues an event received from another process, or a junction event from this one. A
    /// data-change signal rides the same transport but is not a train event: it names no train, so
    /// it is not sent to clients. A junction event is queued only when the sink was configured with
    /// <c>WithJunctionEvents()</c>.
    /// </summary>
    public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct) =>
        message.EventType == TrainLifecycleEventMessage.DataChangedEventType
            ? Task.CompletedTask
            : DispatchAsync(message, ct);

    /// <summary>
    /// Queues the event for the background sender and returns without waiting for delivery.
    /// </summary>
    internal Task DispatchAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (!_config.Matches(message))
            return Task.CompletedTask;

        // A completed queue refuses the write once shutdown started; that is not a drop caused
        // by slow clients, so it is not counted.
        if (!_queue.TryWrite(message, out var dropped) || dropped is null)
            return Task.CompletedTask;

        Interlocked.Increment(ref _dropped);
        if (Interlocked.Increment(ref _droppedSinceReport) == 1)
        {
            _logger?.LogWarning(
                "SignalR sink delivery queue is full ({Capacity} events): dropping {EventType} for "
                    + "train {TrainName} ({ExternalId}) and further events until clients catch up. "
                    + "Train events are kept in preference to junction events.",
                _config.DeliveryQueueCapacity,
                dropped.EventType,
                dropped.TrainName,
                dropped.ExternalId
            );
        }

        return Task.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (await _queue.ReadAsync(_abandon.Token) is { } message)
            {
                await SendAsync(message);

                if (_queue.Count == 0)
                {
                    var dropped = Interlocked.Exchange(ref _droppedSinceReport, 0);
                    if (dropped > 0)
                    {
                        _logger?.LogWarning(
                            "SignalR sink delivery queue drained after dropping {Dropped} events.",
                            dropped
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
        {
            // Shutdown gave up waiting for the queue to drain.
        }
    }

    private async Task SendAsync(TrainLifecycleEventMessage message)
    {
        try
        {
            if (SignalRSinkConfiguration.IsJunctionEvent(message))
            {
                // Matches already refused it unless junction events were asked for; one with no
                // step has nothing to send.
                if (message.Junction is not null)
                    await _hub.Clients.All.JunctionEvent(_config.JunctionProjection(message));
                return;
            }

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

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Stops accepting events and waits for the queued ones to be sent. When
    /// <paramref name="cancellationToken"/> fires first, the rest of the queue is abandoned.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 1);
        _queue.Complete();

        try
        {
            await _sender.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _abandon.CancelAsync();
            _logger?.LogWarning(
                "SignalR sink stopped before its delivery queue drained; {Remaining} events were not sent.",
                _queue.Count
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        using (var timeout = new CancellationTokenSource(DisposeDrainTimeout))
        {
            await StopAsync(timeout.Token);
        }

        _abandon.Dispose();
    }

    /// <summary>
    /// Stops accepting events and abandons whatever is still queued, without blocking. A host
    /// drains the queue through <see cref="StopAsync"/> before its container is disposed.
    /// </summary>
    public void Dispose()
    {
        Volatile.Write(ref _stopped, 1);
        _queue.Complete();
        if (!_sender.IsCompleted)
            _abandon.Cancel();
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
        )
        {
            FailureException = metadata.FailureException,
        };
}
