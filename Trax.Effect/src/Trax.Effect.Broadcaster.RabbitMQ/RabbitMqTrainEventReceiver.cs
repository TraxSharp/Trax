using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Receives train lifecycle events from a RabbitMQ fanout exchange.
/// Each receiver instance creates its own exclusive, auto-delete queue
/// so multiple hub instances can independently consume all events.
/// </summary>
public class RabbitMqTrainEventReceiver : ITrainEventReceiver
{
    private readonly RabbitMqBroadcasterOptions _options;
    private readonly ILogger<RabbitMqTrainEventReceiver>? _logger;

    private IConnection? _connection;
    private IChannel? _channel;
    private string? _queueName;

    /// <summary>
    /// Creates a receiver for the exchange and connection named in <paramref name="options"/>.
    /// Opens no connection until <see cref="StartAsync"/>.
    /// </summary>
    /// <param name="options">The connection URI, exchange name and prefetch count.</param>
    /// <param name="logger">Optional logger for start, stop and handler failures.</param>
    public RabbitMqTrainEventReceiver(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventReceiver>? logger = null
    )
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Connects, declares the fanout exchange, binds a new exclusive auto-delete queue to it and
    /// starts consuming, passing each deserialized event to <paramref name="handler"/>.
    /// </summary>
    /// <param name="handler">
    /// Called once per event. A delivery is acknowledged after the handler returns; if the handler
    /// or deserialization throws, the error is logged and the delivery is rejected without requeue,
    /// so that event is lost to this receiver. A body that deserializes to null is acknowledged and
    /// skipped.
    /// </param>
    /// <param name="ct">Cancels the startup calls, and is also the token passed to every handler call.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="RabbitMqBroadcasterOptions.PrefetchCount"/> is 0.
    /// </exception>
    /// <remarks>Call once per instance; calling again opens a second connection and leaks the first.</remarks>
    public async Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    )
    {
        if (_options.PrefetchCount == 0)
        {
            throw new InvalidOperationException(
                "RabbitMqBroadcasterOptions.PrefetchCount must be at least 1. "
                    + "A prefetch count of 0 means no limit, which lets unacknowledged events pile up "
                    + "in the receiving process."
            );
        }

        var factory = new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        // Bound how many deliveries the broker pushes before this receiver acknowledges them.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.PrefetchCount,
            global: false,
            cancellationToken: ct
        );

        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );

        var queueDeclareResult = await _channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: ct
        );
        _queueName = queueDeclareResult.QueueName;

        await _channel.QueueBindAsync(
            queue: _queueName,
            exchange: _options.ExchangeName,
            routingKey: string.Empty,
            cancellationToken: ct
        );

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(ea.Body.Span);

                if (message is not null)
                {
                    await handler(message, ct);
                }

                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing lifecycle event from RabbitMQ.");
                await _channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: ct
                );
            }
        };

        await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger?.LogInformation(
            "RabbitMQ receiver started on exchange {Exchange}, queue {Queue}.",
            _options.ExchangeName,
            _queueName
        );
    }

    /// <summary>
    /// Deletes this receiver's queue and closes the channel and connection if they are open.
    /// Does not dispose them; <see cref="DisposeAsync"/> does. A channel or connection the broker
    /// has already closed, including one it closes while this runs, is left as it is: the queue
    /// is exclusive and auto-delete, so the broker removes it with the connection.
    /// </summary>
    /// <param name="ct">Cancels the close calls.</param>
    public async Task StopAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            if (_queueName is not null)
                await IgnoringClosed(() =>
                    _channel.QueueDeleteAsync(_queueName, cancellationToken: ct)
                );

            await IgnoringClosed(() => _channel.CloseAsync(cancellationToken: ct));
        }

        if (_connection is { IsOpen: true })
            await IgnoringClosed(() => _connection.CloseAsync(cancellationToken: ct));

        _logger?.LogInformation("RabbitMQ receiver stopped.");
    }

    /// <summary>
    /// Closes the channel and connection if still open, then disposes both. Tolerates either
    /// having been closed by the broker already.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            if (_channel.IsOpen)
                await IgnoringClosed(() => _channel.CloseAsync());
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            if (_connection.IsOpen)
                await IgnoringClosed(() => _connection.CloseAsync());
            _connection.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // IsOpen is only a snapshot: the broker can close the connection between the check and the
    // call. Shutting down something already shut down is not an error.
    private async Task IgnoringClosed(Func<Task> close)
    {
        try
        {
            await close();
        }
        catch (Exception ex) when (ex is AlreadyClosedException or ObjectDisposedException)
        {
            _logger?.LogDebug(ex, "RabbitMQ receiver connection was already closed.");
        }
    }
}
