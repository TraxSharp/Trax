using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// Hosted service that consumes train lifecycle events from an <see cref="ITrainEventReceiver"/>
/// and dispatches them to all registered <see cref="ITrainEventHandler"/> instances.
/// Events this host published are skipped to prevent double-notification when a train runs
/// locally (already handled by in-process lifecycle hooks). A host is recognised by its instance
/// id, not its executor name, so replicas of one app still receive each other's events.
/// </summary>
public class TrainEventReceiverService : BackgroundService
{
    private readonly ITrainEventReceiver _receiver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrainEventReceiverService>? _logger;
    private readonly string _localInstanceId;

    /// <summary>
    /// Creates the service. Registered as a hosted service by <c>UseBroadcaster()</c>. The events it skips as
    /// its own are those stamped with the instance id <c>UseBroadcaster()</c> registered in
    /// <paramref name="serviceProvider"/>.
    /// </summary>
    /// <param name="receiver">The transport-specific receiver.</param>
    /// <param name="serviceProvider">Root provider; a scope is created per message to resolve handlers.</param>
    /// <param name="logger">Optional logging of connection failures and skipped events.</param>
    public TrainEventReceiverService(
        ITrainEventReceiver receiver,
        IServiceProvider serviceProvider,
        ILogger<TrainEventReceiverService>? logger = null
    )
    {
        _receiver = receiver;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _localInstanceId = (
            serviceProvider.GetService<BroadcastInstance>() ?? BroadcastInstance.Unregistered
        ).Id;
    }

    /// <summary>
    /// Starts the receiver and keeps it running until the host stops. When <see cref="ITrainEventReceiver.StartAsync"/>
    /// throws, stops the receiver and retries after 5 seconds, doubling to at most 2 minutes. Each message stamped
    /// with this host's instance id is skipped; every other one goes to all registered
    /// <see cref="ITrainEventHandler"/>s in a fresh scope, with each handler's exception logged and swallowed.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host stops.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("TrainEventReceiverService starting.");

        var delay = TimeSpan.FromSeconds(5);
        var maxDelay = TimeSpan.FromMinutes(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _receiver.StartAsync(
                    async (message, ct) =>
                    {
                        if (IsLocalEvent(message))
                        {
                            _logger?.LogDebug(
                                "Skipping local event {EventType} for train {TrainName} ({ExternalId}).",
                                message.EventType,
                                message.TrainName,
                                message.ExternalId
                            );
                            return;
                        }

                        _logger?.LogDebug(
                            "Received remote event {EventType} for train {TrainName} ({ExternalId}) from {Executor}.",
                            message.EventType,
                            message.TrainName,
                            message.ExternalId,
                            message.Executor
                        );

                        await using var scope = _serviceProvider.CreateAsyncScope();
                        var handlers = scope.ServiceProvider.GetServices<ITrainEventHandler>();

                        foreach (var handler in handlers)
                        {
                            try
                            {
                                await handler.HandleAsync(message, ct);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(
                                    ex,
                                    "TrainEventHandler ({HandlerType}) threw while handling {EventType} for train {TrainName}.",
                                    handler.GetType().Name,
                                    message.EventType,
                                    message.TrainName
                                );
                            }
                        }
                    },
                    stoppingToken
                );

                // StartAsync returned normally — reset delay and wait for cancellation
                delay = TimeSpan.FromSeconds(5);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "TrainEventReceiverService connection failed. Retrying in {Delay}.",
                    delay
                );

                try
                {
                    await _receiver.StopAsync(CancellationToken.None);
                }
                catch
                {
                    // Best-effort cleanup
                }

                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds)
                );
            }
        }
    }

    /// <summary>
    /// Stops the receiver, then the background loop.
    /// </summary>
    /// <param name="cancellationToken">Bounds the shutdown.</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("TrainEventReceiverService stopping.");
        await _receiver.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private bool IsLocalEvent(TrainLifecycleEventMessage message) =>
        string.Equals(message.InstanceId, _localInstanceId, StringComparison.Ordinal);
}
