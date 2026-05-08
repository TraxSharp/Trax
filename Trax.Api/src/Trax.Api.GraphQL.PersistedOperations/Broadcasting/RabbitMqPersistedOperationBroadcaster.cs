using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Publishes <see cref="PersistedOperationChangedMessage"/> to a fanout
/// exchange so every other node clears its local cache entry. Wired only
/// when the consumer calls <c>UseRabbitMqInvalidation()</c>.
/// </summary>
internal sealed class RabbitMqPersistedOperationBroadcaster
    : IPersistedOperationBroadcaster,
        IAsyncDisposable
{
    /// <summary>
    /// Exchange name. Constant so producers and receivers across nodes
    /// rendezvous without configuration. The name is namespaced under
    /// <c>trax.</c> to avoid collisions with the train-broadcaster exchange.
    /// </summary>
    internal const string ExchangeName = "trax.persisted_operations.invalidation";

    private readonly PersistedOperationsOptions _options;
    private readonly ILogger<RabbitMqPersistedOperationBroadcaster> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;

    public RabbitMqPersistedOperationBroadcaster(
        PersistedOperationsOptions options,
        ILogger<RabbitMqPersistedOperationBroadcaster> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrEmpty(options.RabbitMqConnectionString))
            throw new InvalidOperationException(
                "RabbitMqPersistedOperationBroadcaster requires a non-empty connection string."
            );

        _options = options;
        _logger = logger;
    }

    public async Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct)
    {
        var channel = await EnsureChannelAsync(ct).ConfigureAwait(false);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Transient,
        };

        await channel
            .BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct
            )
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Published persisted-operation change ({ChangeType}) for id {Id} to exchange {Exchange}.",
            message.ChangeType,
            message.Id,
            ExchangeName
        );
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _connectionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            if (_connection is not { IsOpen: true })
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_options.RabbitMqConnectionString!),
                };
                _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            }

            _channel = await _connection
                .CreateChannelAsync(cancellationToken: ct)
                .ConfigureAwait(false);

            if (!_exchangeDeclared)
            {
                await _channel
                    .ExchangeDeclareAsync(
                        exchange: ExchangeName,
                        type: ExchangeType.Fanout,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
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

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
