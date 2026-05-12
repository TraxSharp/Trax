using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Listens for <see cref="PersistedOperationChangedMessage"/> events on the
/// RabbitMQ fanout exchange and invalidates the local
/// <see cref="IPersistedOperationCache"/> entry for the affected id.
/// </summary>
/// <remarks>
/// Each node binds an exclusive, auto-delete queue to the fanout, mirroring
/// the train-event receiver pattern in <c>Trax.Effect.Broadcaster.RabbitMQ</c>.
/// Wired only when the consumer calls <c>UseRabbitMqInvalidation()</c>.
/// </remarks>
internal sealed class PersistedOperationReceiverService : IHostedService, IAsyncDisposable
{
    private readonly PersistedOperationsOptions _options;
    private readonly IPersistedOperationCache _cache;
    private readonly HotChocolateOperationCacheInvalidator _hcInvalidator;
    private readonly ILogger<PersistedOperationReceiverService> _logger;

    private IConnection? _connection;
    private IChannel? _channel;
    private string? _queueName;

    public PersistedOperationReceiverService(
        PersistedOperationsOptions options,
        IPersistedOperationCache cache,
        HotChocolateOperationCacheInvalidator hcInvalidator,
        ILogger<PersistedOperationReceiverService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(hcInvalidator);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _cache = cache;
        _hcInvalidator = hcInvalidator;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RabbitMqConnectionString))
        {
            // Hosted service was registered but no connection string is
            // configured. Skip silently rather than crash the host.
            return;
        }

        var factory = new ConnectionFactory { Uri = new Uri(_options.RabbitMqConnectionString) };
        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _channel
            .ExchangeDeclareAsync(
                exchange: RabbitMqPersistedOperationBroadcaster.ExchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var queue = await _channel
            .QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        _queueName = queue.QueueName;

        await _channel
            .QueueBindAsync(
                queue: _queueName,
                exchange: RabbitMqPersistedOperationBroadcaster.ExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageAsync;

        await _channel
            .BasicConsumeAsync(
                queue: _queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Persisted-operation invalidation receiver started on queue {Queue}.",
            _queueName
        );
    }

    private async Task OnMessageAsync(object _, BasicDeliverEventArgs ea)
    {
        if (_channel is null)
            return;

        try
        {
            var message = JsonSerializer.Deserialize<PersistedOperationChangedMessage>(
                ea.Body.Span
            );

            if (message is not null)
            {
                _cache.Invalidate(message.TenantKey, message.Id);
                await _hcInvalidator.InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await _channel
                .BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to process persisted-operation invalidation message; nack-ing without requeue."
            );
            try
            {
                await _channel
                    .BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: false,
                        cancellationToken: CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch
            {
                // Channel may already be closed during shutdown; nothing else to do.
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            if (_queueName is not null)
            {
                try
                {
                    await _channel
                        .QueueDeleteAsync(_queueName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Auto-delete queues vanish on disconnect; explicit delete is best-effort.
                }
            }

            await _channel.CloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (_connection is { IsOpen: true })
            await _connection
                .CloseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        _logger.LogInformation("Persisted-operation invalidation receiver stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            if (_channel.IsOpen)
                await _channel.CloseAsync().ConfigureAwait(false);
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            if (_connection.IsOpen)
                await _connection.CloseAsync().ConfigureAwait(false);
            _connection.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
