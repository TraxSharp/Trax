using System.Text.Json;
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
/// sender publishes the queue in order. When the queue is full it gives up a junction event
/// before any train event, and a non-terminal train event (<c>Started</c>, <c>StateChanged</c>,
/// <c>DataChanged</c>) before a terminal one (<c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>): an
/// incoming junction event is dropped, and any other incoming event replaces the oldest queued
/// event of a lower rank. Every drop is counted and logged, and the train carries on.
/// </para>
/// <para>
/// Each publish waits for the broker's confirm, bounded by <see cref="PublishTimeout"/>. An event
/// the broker has not confirmed is sent again, so a receiver can see an event twice but does not
/// silently miss one the sender believed it sent. While the broker cannot be reached the sender
/// retries the event it holds with a growing delay, each connection attempt bounded by
/// <see cref="ConnectTimeout"/>, and events queue behind it. An event the broker refuses (it
/// closes the channel on it, or rejects the publish) is tried <see cref="MaxRefusedAttempts"/>
/// times and then dropped and logged as an error, so a refusal that will not clear, such as an
/// exchange declared elsewhere with another type, does not hold up the events behind it.
/// </para>
/// <para>
/// The connection and channel are opened lazily, and reopened when found closed; a closed one is
/// disposed before it is replaced. A publish that fails discards its channel, and one that times
/// out discards the connection too, since its socket may be dead while it still reports open. The
/// exchange is declared on every channel the sender opens, so one deleted or lost with a broker
/// restart is recreated. The connection does not recover itself, because the sender reconnects on
/// demand. The exchange is declared durable, but messages are published transient and
/// unmandatory: an event published while no receiver queue is bound is dropped by the broker.
/// </para>
/// </remarks>
internal class RabbitMqTrainEventBroadcaster : ITrainEventBroadcaster, IAsyncDisposable
{
    /// <summary>How many events may wait for the sender before further ones are dropped.</summary>
    internal const int DefaultQueueCapacity = 1024;

    /// <summary>How many times an event the broker refuses is tried before it is dropped.</summary>
    internal const int MaxRefusedAttempts = 3;

    /// <summary>The longest a single connection attempt may take.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The longest one publish may wait for the broker's confirm before it is retried.</summary>
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long disposal waits for queued events to be sent before abandoning them. Disposal does
    /// not wait at all while the sender is failing to reach the broker.
    /// </summary>
    internal static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long junction events are dropped after the first failure to send one.</summary>
    internal static readonly TimeSpan FirstJunctionBackoff = TimeSpan.FromSeconds(1);

    /// <summary>The longest junction events are dropped after a failure before one is tried again.</summary>
    internal static readonly TimeSpan MaxJunctionBackoff = TimeSpan.FromMinutes(1);

    private static readonly CreateChannelOptions ConfirmedChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true
    );

    private readonly RabbitMqBroadcasterOptions _options;
    private readonly ILogger<RabbitMqTrainEventBroadcaster>? _logger;
    private readonly int _queueCapacity;
    private readonly Func<CancellationToken, Task<IConnection>> _connect;
    private readonly TimeSpan _publishTimeout;
    private readonly TimeSpan _firstRetryDelay;
    private readonly LifecycleEventQueue _queue;
    private readonly CancellationTokenSource _abandon = new();
    private readonly Task _sender;

    private IConnection? _connection;
    private IChannel? _channel;
    private IChannel? _junctionChannel;
    private int _junctionFailing;
    private TimeSpan _junctionBackoff;
    private DateTime _junctionUnavailableUntil = DateTime.MinValue;
    private long _dropped;
    private long _droppedSinceReport;
    private long _refused;
    private int _failing;
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
        int queueCapacity,
        Func<CancellationToken, Task<IConnection>>? connect = null,
        TimeSpan? publishTimeout = null,
        TimeSpan? firstRetryDelay = null
    )
    {
        _options = options;
        _logger = logger;
        _queueCapacity = queueCapacity;
        _connect = connect ?? ConnectAsync;
        _publishTimeout = publishTimeout ?? PublishTimeout;
        _firstRetryDelay = firstRetryDelay ?? FirstRetryDelay;
        _queue = new LifecycleEventQueue(queueCapacity);
        _sender = Task.Run(SendLoopAsync);
    }

    /// <summary>
    /// Events dropped because the queue was full, since the broadcaster was created. Counts both an
    /// incoming event that was dropped and a queued one an incoming terminal event replaced.
    /// </summary>
    internal long DroppedEvents => Interlocked.Read(ref _dropped);

    /// <summary>Events dropped because the broker refused them <see cref="MaxRefusedAttempts"/> times.</summary>
    internal long RefusedEvents => Interlocked.Read(ref _refused);

    /// <summary>The clock the junction backoff is measured on. For tests.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Until when junction events are dropped without being tried, after a failure.</summary>
    internal DateTime JunctionUnavailableUntil => _junctionUnavailableUntil;

    /// <summary>
    /// Queues <paramref name="message"/> for the background sender and returns without waiting
    /// for the broker. When the queue is full an event is dropped and counted, by the policy in the
    /// class remarks, and the first drop of a run is logged as a warning.
    /// </summary>
    /// <param name="message">The lifecycle event to publish.</param>
    /// <param name="ct">Not used: queueing never waits.</param>
    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        // A completed queue refuses the write once disposal started; that is not a drop.
        if (!_queue.TryWrite(message, out var dropped) || dropped is null)
            return Task.CompletedTask;

        Interlocked.Increment(ref _dropped);
        if (Interlocked.Increment(ref _droppedSinceReport) == 1)
        {
            _logger?.LogWarning(
                "RabbitMQ broadcaster queue is full ({Capacity} events): dropping {EventType} for "
                    + "train {TrainName} ({ExternalId}) and further events until the broker catches up. "
                    + "Train events are kept in preference to junction events, and terminal ones in "
                    + "preference to Started and other non-terminal ones.",
                _queueCapacity,
                dropped.EventType,
                dropped.TrainName,
                dropped.ExternalId
            );
        }

        return Task.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        var ct = _abandon.Token;
        try
        {
            while (await _queue.ReadAsync(ct) is { } message)
            {
                // A junction event goes on a channel and exchange of their own, and is given up at
                // once when it cannot be sent, so it never holds up or closes the train events.
                if (IsJunctionEvent(message))
                    await SendJunctionAsync(message, ct);
                else
                    await SendWithRetryAsync(message, ct);

                if (_queue.Count == 0)
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
            // Disposal gave up on the queue.
        }
    }

    private async Task SendWithRetryAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var delay = _firstRetryDelay;
        var failures = 0;
        var refusals = 0;
        while (true)
        {
            try
            {
                await SendAsync(message, ct);
                Interlocked.Exchange(ref _failing, 0);
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
                var refused = IsRefusal(ex);
                // An attempt that timed out may be on a dead socket that still reports open.
                await DiscardAsync(connectionToo: ex is OperationCanceledException);

                if (refused && ++refusals >= MaxRefusedAttempts)
                {
                    Interlocked.Increment(ref _refused);
                    _logger?.LogError(
                        ex,
                        "RabbitMQ broker refused {EventType} for train {TrainName} ({ExternalId}) "
                            + "{Attempts} times on exchange {Exchange}; dropping it and sending the events behind it.",
                        message.EventType,
                        message.TrainName,
                        message.ExternalId,
                        refusals,
                        _options.ExchangeName
                    );
                    return;
                }

                // Disposal does not wait on a broker the sender cannot reach. Set before reading
                // _disposed, which DisposeAsync sets before reading this: one of the two sees the other.
                Interlocked.Exchange(ref _failing, 1);
                if (Volatile.Read(ref _disposed) != 0)
                {
                    await _abandon.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }

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

    private static bool IsJunctionEvent(TrainLifecycleEventMessage message) =>
        message.Junction is not null
        || TrainLifecycleEventMessage.IsJunctionEvent(message.EventType);

    // The broker answered and turned this event down: it closed the channel with a channel-level
    // error (access refused, not found, not allowed, precondition failed), or nacked the publish.
    private static bool IsRefusal(Exception ex) =>
        ex switch
        {
            PublishException => true,
            OperationInterruptedException
            {
                ShutdownReason:
                { Initiator: ShutdownInitiator.Peer, ReplyCode: 403 or 404 or 405 or 406 },
            } => true,
            _ => false,
        };

    private async Task SendJunctionAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        // After a failure, junction events are dropped without opening a channel or declaring the
        // exchange until the backoff passes, so a junction exchange that cannot be used costs the
        // sender, and the train events behind it, one attempt per backoff rather than one per step.
        if (UtcNow() < _junctionUnavailableUntil)
        {
            _logger?.LogDebug("RabbitMQ broadcaster dropped a junction event while backing off.");
            return;
        }

        try
        {
            await SendAsync(message, ct);
            _junctionBackoff = TimeSpan.Zero;
            _junctionUnavailableUntil = DateTime.MinValue;
            if (Interlocked.Exchange(ref _junctionFailing, 0) == 1)
                _logger?.LogInformation(
                    "RabbitMQ broadcaster is publishing junction events to {Exchange} again.",
                    _options.EffectiveJunctionExchangeName
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await DisposeQuietlyAsync(_junctionChannel);
            _junctionChannel = null;

            _junctionBackoff =
                _junctionBackoff == TimeSpan.Zero ? FirstJunctionBackoff
                : _junctionBackoff * 2 > MaxJunctionBackoff ? MaxJunctionBackoff
                : _junctionBackoff * 2;
            _junctionUnavailableUntil = UtcNow() + _junctionBackoff;

            // One warning per outage; the steps lost inside it are Debug.
            if (Interlocked.Exchange(ref _junctionFailing, 1) == 0)
                _logger?.LogWarning(
                    ex,
                    "RabbitMQ broadcaster cannot publish junction events to exchange {Exchange}; "
                        + "dropping them until it can, trying again after a growing pause of up to "
                        + "a minute. Train events are not affected.",
                    _options.EffectiveJunctionExchangeName
                );
            else
                _logger?.LogDebug(ex, "RabbitMQ broadcaster dropped a junction event.");
        }
    }

    private async Task SendAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var junction = IsJunctionEvent(message);
        var channel = await EnsureChannelAsync(junction, ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Transient,
        };

        // The channel tracks confirms, so this completes once the broker has confirmed the event.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(_publishTimeout);
        // A junction event goes to an exchange of its own, which only receivers that know junction
        // events bind, so one that predates them never receives one.
        var exchange = junction ? _options.EffectiveJunctionExchangeName : _options.ExchangeName;

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: bound.Token
        );

        _logger?.LogDebug(
            "Published {EventType} event for train {TrainName} to exchange {Exchange}.",
            message.EventType,
            message.TrainName,
            exchange
        );
    }

    // Only the sender calls this, so no lock is needed. Train events and junction events each have
    // a channel of their own, which declares only its own exchange, so a junction exchange the
    // broker refuses closes the junction channel and never the train one. The junction exchange is
    // declared only once there is a junction event to send.
    private async Task<IChannel> EnsureChannelAsync(bool junction, CancellationToken ct)
    {
        var current = junction ? _junctionChannel : _channel;
        if (current is { IsOpen: true })
            return current;

        await DisposeQuietlyAsync(current);
        if (junction)
            _junctionChannel = null;
        else
            _channel = null;

        if (_connection is not { IsOpen: true })
        {
            await DisposeQuietlyAsync(_connection);
            _connection = null;
            _connection = await _connect(ct);
        }

        // Opening a channel waits on the broker too, so it has the same bound as a publish.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(_publishTimeout);
        var channel = await _connection.CreateChannelAsync(ConfirmedChannel, bound.Token);
        try
        {
            // Declared on every channel: the exchange may have been deleted, or lost with a broker
            // restart, since the last one.
            await channel.ExchangeDeclareAsync(
                exchange: junction ? _options.EffectiveJunctionExchangeName : _options.ExchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: bound.Token
            );
        }
        catch
        {
            await DisposeQuietlyAsync(channel);
            throw;
        }

        if (junction)
            _junctionChannel = channel;
        else
            _channel = channel;
        return channel;
    }

    private async Task<IConnection> ConnectAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString),
            RequestedConnectionTimeout = ConnectTimeout,
            AutomaticRecoveryEnabled = false,
        };
        return await factory.CreateConnectionAsync(ct);
    }

    private async Task DiscardAsync(bool connectionToo)
    {
        await DisposeQuietlyAsync(_channel);
        _channel = null;

        if (connectionToo)
        {
            await DisposeQuietlyAsync(_junctionChannel);
            _junctionChannel = null;

            _junctionBackoff =
                _junctionBackoff == TimeSpan.Zero ? FirstJunctionBackoff
                : _junctionBackoff * 2 > MaxJunctionBackoff ? MaxJunctionBackoff
                : _junctionBackoff * 2;
            _junctionUnavailableUntil = UtcNow() + _junctionBackoff;
            await DisposeQuietlyAsync(_connection);
            _connection = null;
        }
    }

    // Closes an open channel or connection within CloseTimeout and disposes it. A dead socket
    // cannot hold this up, and one the broker closed first is not an error.
    private static async Task DisposeQuietlyAsync(IDisposable? closed)
    {
        if (closed is null)
            return;

        try
        {
            using var bound = new CancellationTokenSource(CloseTimeout);
            switch (closed)
            {
                case IChannel { IsOpen: true } channel:
                    await channel.CloseAsync(
                        Constants.ReplySuccess,
                        "Goodbye",
                        abort: false,
                        bound.Token
                    );
                    break;
                case IConnection { IsOpen: true } connection:
                    await connection.CloseAsync(
                        Constants.ReplySuccess,
                        "Goodbye",
                        CloseTimeout,
                        abort: false,
                        bound.Token
                    );
                    break;
            }
        }
        catch (Exception ex)
            when (ex
                    is AlreadyClosedException
                        or ObjectDisposedException
                        or OperationCanceledException
            )
        {
            // Closed by the broker between the check and the call, or its socket is gone.
        }

        closed.Dispose();
    }

    /// <summary>
    /// Stops accepting events, waits up to <see cref="DisposeDrainTimeout"/> for the queued ones to
    /// be sent, abandons the rest, then closes and disposes the channel and connection. While the
    /// sender is failing to reach the broker it does not wait: the queue is abandoned at once.
    /// Safe to call more than once, and after the broker or the client library already closed them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Complete();
        if (Volatile.Read(ref _failing) != 0)
            await _abandon.CancelAsync();

        try
        {
            await _sender.WaitAsync(DisposeDrainTimeout);
        }
        catch (TimeoutException)
        {
            await _abandon.CancelAsync();
            await _sender;
        }

        var remaining = _queue.Count;
        if (remaining > 0)
            _logger?.LogWarning(
                "RabbitMQ broadcaster stopped before its queue drained; {Remaining} events were not sent.",
                remaining
            );

        await DisposeQuietlyAsync(_channel);
        await DisposeQuietlyAsync(_junctionChannel);
        await DisposeQuietlyAsync(_connection);
        _abandon.Dispose();
        GC.SuppressFinalize(this);
    }
}
