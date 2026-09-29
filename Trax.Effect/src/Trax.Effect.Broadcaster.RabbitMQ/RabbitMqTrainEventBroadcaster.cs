using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Publishes train lifecycle events to a RabbitMQ fanout exchange.
/// Infrastructure registered by <c>UseRabbitMq</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>
/// The connection and channel are opened lazily on the first publish, reopened if they are found
/// closed, and shared by concurrent publishers. The exchange is declared durable, but messages are
/// published transient and unmandatory: an event published while no receiver queue is bound is
/// dropped by the broker.
/// </remarks>
internal class RabbitMqTrainEventBroadcaster : ITrainEventBroadcaster, IAsyncDisposable
{
    private readonly RabbitMqBroadcasterOptions _options;
    private readonly ILogger<RabbitMqTrainEventBroadcaster>? _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;

    /// <summary>
    /// Creates a broadcaster for the exchange and connection named in <paramref name="options"/>.
    /// Opens no connection.
    /// </summary>
    /// <param name="options">The connection URI and exchange name.</param>
    /// <param name="logger">Optional logger; each publish is logged at Debug.</param>
    public RabbitMqTrainEventBroadcaster(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventBroadcaster>? logger = null
    )
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Serializes <paramref name="message"/> to JSON and publishes it to the fanout exchange,
    /// connecting and declaring the exchange first if needed.
    /// </summary>
    /// <param name="message">The lifecycle event to publish.</param>
    /// <param name="ct">Cancels connecting and publishing.</param>
    /// <remarks>Connection and publish failures propagate to the caller.</remarks>
    public async Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var channel = await EnsureChannelAsync(ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Transient,
        };

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct
        );

        _logger?.LogDebug(
            "Published {EventType} event for train {TrainName} to exchange {Exchange}.",
            message.EventType,
            message.TrainName,
            _options.ExchangeName
        );
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            if (_connection is not { IsOpen: true })
            {
                var factory = new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
                _connection = await factory.CreateConnectionAsync(ct);
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            if (!_exchangeDeclared)
            {
                await _channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct
                );
                _exchangeDeclared = true;
            }

            return _channel;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Closes and disposes the channel and connection, if they were opened. Safe to call after the
    /// client library has already disposed them on connection loss.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // RabbitMQ.Client's AutorecoveringChannel/Connection can dispose
        // themselves on connection loss before this method runs (e.g.
        // when the host shuts down and the connection drops first).
        // CloseAsync on an already-disposed channel/connection throws
        // ObjectDisposedException. Disposal must be idempotent, so we
        // swallow that specific exception. Dispose() itself is idempotent
        // by IDisposable contract — no try/catch needed around it.
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch (ObjectDisposedException) { }
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (ObjectDisposedException) { }
            _connection.Dispose();
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
