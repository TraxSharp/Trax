using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Publishes train lifecycle events to a RabbitMQ fanout exchange.
/// Infrastructure registered by <c>UseRabbitMq</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>
/// <para>
/// A train awaits its lifecycle hooks inline, so publishing never waits on the broker.
/// <see cref="PublishAsync"/> writes the event to a bounded queue and returns; one background
/// sender publishes the queue in order. When the queue is full the event is dropped, counted and
/// logged, and the train carries on. While the broker cannot be reached the sender retries the
/// event it holds with a growing delay, each connection attempt bounded by
/// <see cref="ConnectTimeout"/>, and events queue behind it up to the queue's capacity.
/// </para>
/// <para>
/// The connection and channel are opened lazily, and reopened when found closed; a closed one is
/// disposed before it is replaced. The connection does not recover itself, because the sender
/// reconnects on demand. The exchange is declared durable, but messages are published transient
/// and unmandatory: an event published while no receiver queue is bound is dropped by the broker.
/// </para>
/// </remarks>
internal class RabbitMqTrainEventBroadcaster : ITrainEventBroadcaster, IAsyncDisposable
{
    /// <summary>How many events may wait for the sender before further ones are dropped.</summary>
    internal const int DefaultQueueCapacity = 1024;

    /// <summary>The longest a single connection attempt may take.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long disposal waits for queued events to be sent before abandoning them.</summary>
    internal static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly RabbitMqBroadcasterOptions _options;
    private readonly ILogger<RabbitMqTrainEventBroadcaster>? _logger;
    private readonly int _queueCapacity;
    private readonly Channel<TrainLifecycleEventMessage> _queue;
    private readonly CancellationTokenSource _abandon = new();
    private readonly Task _sender;

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;
    private long _dropped;
    private long _droppedSinceReport;
    private int _disposed;

    /// <summary>
    /// Creates a broadcaster for the exchange and connection named in <paramref name="options"/>
    /// and starts its background sender. Opens no connection until there is an event to send.
    /// </summary>
    /// <param name="options">The connection URI and exchange name.</param>
    /// <param name="logger">Optional logger; each publish is logged at Debug.</param>
    public RabbitMqTrainEventBroadcaster(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventBroadcaster>? logger = null
    )
        : this(options, logger, DefaultQueueCapacity) { }

    internal RabbitMqTrainEventBroadcaster(
        RabbitMqBroadcasterOptions options,
        ILogger<RabbitMqTrainEventBroadcaster>? logger,
        int queueCapacity
    )
    {
        _options = options;
        _logger = logger;
        _queueCapacity = queueCapacity;
        _queue = Channel.CreateBounded<TrainLifecycleEventMessage>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );
        _sender = Task.Run(SendLoopAsync);
    }

    /// <summary>Events dropped because the queue was full, since the broadcaster was created.</summary>
    internal long DroppedEvents => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Queues <paramref name="message"/> for the background sender and returns without waiting
    /// for the broker. When the queue is full the event is dropped and counted, and the first drop
    /// of a run is logged as a warning.
    /// </summary>
    /// <param name="message">The lifecycle event to publish.</param>
    /// <param name="ct">Not used: queueing never waits.</param>
    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (_queue.Writer.TryWrite(message))
            return Task.CompletedTask;

        // TryWrite also fails once disposal completed the queue; that is not a drop.
        if (Volatile.Read(ref _disposed) != 0)
            return Task.CompletedTask;

        Interlocked.Increment(ref _dropped);
        if (Interlocked.Increment(ref _droppedSinceReport) == 1)
        {
            _logger?.LogWarning(
                "RabbitMQ broadcaster queue is full ({Capacity} events): dropping {EventType} for "
                    + "train {TrainName} ({ExternalId}) and further events until the broker catches up.",
                _queueCapacity,
                message.EventType,
                message.TrainName,
                message.ExternalId
            );
        }

        return Task.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        var ct = _abandon.Token;
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(ct))
            {
                await SendWithRetryAsync(message, ct);

                if (_queue.Reader.Count == 0)
                {
                    var dropped = Interlocked.Exchange(ref _droppedSinceReport, 0);
                    if (dropped > 0)
                    {
                        _logger?.LogWarning(
                            "RabbitMQ broadcaster queue drained after dropping {Dropped} events.",
                            dropped
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposal gave up waiting for the queue to drain.
        }
    }

    private async Task SendWithRetryAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var delay = FirstRetryDelay;
        var failures = 0;
        while (true)
        {
            try
            {
                await SendAsync(message, ct);
                if (failures > 0)
                    _logger?.LogInformation(
                        "RabbitMQ broadcaster reached the broker again after {Failures} failed attempts.",
                        failures
                    );
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One warning per outage; the attempts inside it are Debug.
                if (failures++ == 0)
                    _logger?.LogWarning(
                        ex,
                        "RabbitMQ broadcaster cannot publish to exchange {Exchange}; retrying. "
                            + "Events queue behind it until the queue is full.",
                        _options.ExchangeName
                    );
                else
                    _logger?.LogDebug(ex, "RabbitMQ broadcaster publish attempt failed.");

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private async Task SendAsync(TrainLifecycleEventMessage message, CancellationToken ct)
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

    // Only the sender calls this, so no lock is needed.
    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await DisposeQuietlyAsync(_channel);
        _channel = null;

        if (_connection is not { IsOpen: true })
        {
            await DisposeQuietlyAsync(_connection);
            _connection = null;

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString),
                RequestedConnectionTimeout = ConnectTimeout,
                AutomaticRecoveryEnabled = false,
            };
            _connection = await factory.CreateConnectionAsync(ct);
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        if (!_exchangeDeclared)
        {
            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: ct
            );
            _exchangeDeclared = true;
        }

        _channel = channel;
        return channel;
    }

    private static async Task DisposeQuietlyAsync(IDisposable? closed)
    {
        if (closed is null)
            return;

        try
        {
            switch (closed)
            {
                case IChannel { IsOpen: true } channel:
                    await channel.CloseAsync();
                    break;
                case IConnection { IsOpen: true } connection:
                    await connection.CloseAsync();
                    break;
            }
        }
        catch (Exception ex) when (ex is AlreadyClosedException or ObjectDisposedException)
        {
            // Closed by the broker between the check and the call.
        }

        closed.Dispose();
    }

    /// <summary>
    /// Stops accepting events, waits up to <see cref="DisposeDrainTimeout"/> for the queued ones to
    /// be sent, abandons the rest, then closes and disposes the channel and connection. Safe to
    /// call more than once, and after the broker or the client library already closed them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Writer.TryComplete();
        try
        {
            await _sender.WaitAsync(DisposeDrainTimeout);
        }
        catch (TimeoutException)
        {
            await _abandon.CancelAsync();
            _logger?.LogWarning(
                "RabbitMQ broadcaster stopped before its queue drained; {Remaining} events were not sent.",
                _queue.Reader.Count
            );
            await _sender;
        }

        await DisposeQuietlyAsync(_channel);
        await DisposeQuietlyAsync(_connection);
        _abandon.Dispose();
        GC.SuppressFinalize(this);
    }
}
