using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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

    public RabbitMqTrainEventReceiver(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventReceiver>? logger = null
    )
    {
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
        CancellationToken ct
    )
    {
        var factory = new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

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

    public async Task StopAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            if (_queueName is not null)
            {
                await _channel.QueueDeleteAsync(_queueName, cancellationToken: ct);
            }

            await _channel.CloseAsync(cancellationToken: ct);
        }

        if (_connection is { IsOpen: true })
        {
            await _connection.CloseAsync(cancellationToken: ct);
        }

        _logger?.LogInformation("RabbitMQ receiver stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            if (_channel.IsOpen)
                await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            if (_connection.IsOpen)
                await _connection.CloseAsync();
            _connection.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
