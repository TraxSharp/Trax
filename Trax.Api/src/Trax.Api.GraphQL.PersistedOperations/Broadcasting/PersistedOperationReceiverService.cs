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
/// RabbitMQ fanout exchange and empties this node's persisted-operation caches: the
/// <see cref="IPersistedOperationCache"/> entry for the affected id, and HotChocolate's
/// document and prepared-operation caches.
/// </summary>
/// <remarks>
/// Each node binds an exclusive, auto-delete queue to the fanout, mirroring
/// the train-event receiver pattern in <c>Trax.Effect.Broadcaster.RabbitMQ</c>.
/// Wired only when the consumer calls <c>UseRabbitMqInvalidation()</c>.
/// <para>
/// A broadcast sent while the connection is down never reaches this node, because its queue
/// goes with the connection. So, as with any pub/sub invalidation channel, losing the connection
/// empties every cache, and so does recovering it: what was cached in between may already be
/// out of date. The broker can also close the channel alone, leaving the connection up, which
/// the client's recovery does not cover: the receiver empties its caches, subscribes again on a
/// new channel (retrying with a growing delay), and empties them once more when it has.
/// </para>
/// </remarks>
internal sealed class PersistedOperationReceiverService : IHostedService, IAsyncDisposable
{
    private readonly PersistedOperationsOptions _options;
    private readonly IPersistedOperationCache _cache;
    private readonly HotChocolateOperationCacheInvalidator _hcInvalidator;
    private readonly ILogger<PersistedOperationReceiverService> _logger;

    private readonly CancellationTokenSource _stopping = new();
    private IConnection? _connection;
    private IChannel? _channel;
    private string? _queueName;

    /// <summary>The channel the receiver currently consumes on. For tests.</summary>
    internal IChannel? Channel => _channel;

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
        _connection.ConnectionShutdownAsync += (_, args) =>
            args.Initiator == ShutdownInitiator.Application
                ? Task.CompletedTask
                : EmptyEveryCacheAsync("the connection to the broker was lost");
        _connection.RecoverySucceededAsync += (_, _) =>
            EmptyEveryCacheAsync("the connection to the broker recovered");
        await ConsumeAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Persisted-operation invalidation receiver started on queue {Queue}.",
            _queueName
        );
    }

    /// <summary>
    /// Opens a channel, binds a fresh server-named queue to the fanout exchange and consumes it.
    /// </summary>
    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var channel = await _connection!
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        channel.ChannelShutdownAsync += (_, args) => OnChannelShutdownAsync(args);

        await channel
            .ExchangeDeclareAsync(
                exchange: _options.RabbitMqExchange,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var queue = await channel
            .QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await channel
            .QueueBindAsync(
                queue: queue.QueueName,
                exchange: _options.RabbitMqExchange,
                routingKey: string.Empty,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        _channel = channel;
        _queueName = queue.QueueName;

        var consumer = new AsyncEventingBasicConsumer(channel);
        // Acknowledged on the channel that delivered it, which may not be the current one.
        consumer.ReceivedAsync += (_, ea) => OnMessageAsync(channel, ea);

        await channel
            .BasicConsumeAsync(
                queue: queue.QueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The broker closed this receiver's channel while the connection stayed up. Connection
    /// recovery does not reopen it, so nothing would be received again: empty the caches and
    /// subscribe again in the background.
    /// </summary>
    private Task OnChannelShutdownAsync(ShutdownEventArgs args)
    {
        if (
            args.Initiator == ShutdownInitiator.Application
            || _stopping.IsCancellationRequested
            || _connection is not { IsOpen: true }
        )
            // Closed by this service, or with the connection, whose own events and recovery
            // cover it.
            return Task.CompletedTask;

        _ = Task.Run(SubscribeAgainAsync);
        return EmptyEveryCacheAsync("the broker closed the receiver's channel");
    }

    private async Task SubscribeAgainAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(_stopping.Token).ConfigureAwait(false);
                await EmptyEveryCacheAsync("the receiver subscribed again on a new channel")
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (!_stopping.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Could not subscribe to persisted-operation invalidations again; retrying in {Delay}.",
                    delay
                );
                try
                {
                    await Task.Delay(delay, _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxResubscribeDelay.Ticks));
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static readonly TimeSpan MaxResubscribeDelay = TimeSpan.FromSeconds(30);

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs ea)
    {
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

            await channel
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
                await channel
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

    /// <summary>
    /// Empties every persisted-operation cache on this node, for when a broadcast may have been
    /// missed.
    /// </summary>
    internal async Task EmptyEveryCacheAsync(string reason)
    {
        _logger.LogWarning(
            "Emptying the persisted-operation caches because {Reason}; a change broadcast meanwhile may not have arrived.",
            reason
        );
        // The invalidator advances the generation, which makes every cached entry on this node,
        // the lookup cache's included, unservable.
        await _hcInvalidator.InvalidateAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

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
        if (!_stopping.IsCancellationRequested)
            await _stopping.CancelAsync().ConfigureAwait(false);

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
