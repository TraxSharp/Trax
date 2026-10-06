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
/// <remarks>
/// The channel runs in publisher-confirm mode, so a publish completes only once the broker has
/// accepted the message, and throws when the broker refuses it or does not answer within
/// <see cref="ConfirmTimeout"/>.
/// </remarks>
internal sealed class RabbitMqPersistedOperationBroadcaster
    : IPersistedOperationBroadcaster,
        IAsyncDisposable
{
    /// <summary>
    /// The default exchange name. Constant so producers and receivers across nodes
    /// rendezvous without configuration. The name is namespaced under
    /// <c>trax.</c> to avoid collisions with the train-broadcaster exchange.
    /// </summary>
    internal const string ExchangeName = "trax.persisted_operations.invalidation";

    /// <summary>How long a publish waits for the broker's confirm before it counts as failed.</summary>
    internal static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);

    private readonly PersistedOperationsOptions _options;
    private readonly ILogger<RabbitMqPersistedOperationBroadcaster> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;

    /// <summary>The channel publishes go out on, once one is open. For tests.</summary>
    internal IChannel? Channel => _channel;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConfirmTimeout);
        ct = timeout.Token;

        var channel = await EnsureChannelAsync(ct).ConfigureAwait(false);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Transient,
        };

        await channel
            .BasicPublishAsync(
                exchange: _options.RabbitMqExchange,
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
            _options.RabbitMqExchange
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
                .CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true
                    ),
                    ct
                )
                .ConfigureAwait(false);

            if (!_exchangeDeclared)
            {
                await _channel
                    .ExchangeDeclareAsync(
                        exchange: _options.RabbitMqExchange,
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
