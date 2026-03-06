using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Publishes train lifecycle events to a RabbitMQ fanout exchange.
/// </summary>
public class RabbitMqTrainEventBroadcaster : ITrainEventBroadcaster, IAsyncDisposable
{
    private readonly RabbitMqBroadcasterOptions _options;
    private readonly ILogger<RabbitMqTrainEventBroadcaster>? _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;

    public RabbitMqTrainEventBroadcaster(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventBroadcaster>? logger = null
    )
    {
        _options = options;
        _logger = logger;
    }

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

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
