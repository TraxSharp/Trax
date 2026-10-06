using System.Runtime.CompilerServices;
using HotChocolate.AspNetCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Ends an accepted socket connection when what it was accepted on runs out: a deadline (the
/// maximum connection lifetime, a JWT's <c>exp</c>, a cookie sign-in's expiry), whichever comes
/// first, or a periodic re-check of its credential that no longer holds. One timer per
/// connection serves all of them, and it goes with the connection.
/// </summary>
/// <remarks>
/// A deadline closes with the reason it was registered with: a credential that expired closes with
/// <see cref="ConnectionCloseReason.PolicyViolation"/> (1008), and the maximum lifetime with
/// <see cref="ConnectionCloseReason.EndpointUnavailable"/> (1001, Going Away), which graphql-ws
/// clients reconnect after. A failed re-check closes with
/// <see cref="ConnectionCloseReason.PolicyViolation"/>. See
/// <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
/// </remarks>
internal sealed class SocketConnectionLifetime
{
    /// <summary>The close message a connection receives at its maximum lifetime.</summary>
    internal const string LifetimeMessage = "The connection reached its maximum lifetime.";

    /// <summary>The close message a connection receives when its credential no longer holds.</summary>
    internal const string RevokedMessage = "The connection's credential is no longer valid.";

    private static readonly ConditionalWeakTable<ISocketConnection, SocketConnectionLifetime> All =
        new();

    // ITimer's largest due time, a little under 50 days. A later deadline is reached in steps.
    private static readonly TimeSpan MaxTimerDue = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly ISocketConnection _connection;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ITimer _timer;
    private readonly Lock _lock = new();

    private DateTimeOffset? _deadline;
    private ConnectionCloseReason _deadlineReason;
    private string _deadlineMessage = LifetimeMessage;

    private Func<CancellationToken, ValueTask<bool>>? _recheck;
    private TimeSpan _recheckInterval;
    private DateTimeOffset _nextRecheck;
    private bool _rechecking;
    private bool _closed;

    private SocketConnectionLifetime(
        ISocketConnection connection,
        TimeProvider time,
        ILogger logger
    )
    {
        _connection = connection;
        _time = time;
        _logger = logger;
        _timer = time.CreateTimer(
            static state => ((SocketConnectionLifetime)state!).OnTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
        connection.RequestAborted.Register(static state => ((ITimer)state!).Dispose(), _timer);
    }

    /// <summary>The lifetime tracker of <paramref name="connection"/>, created on first use.</summary>
    public static SocketConnectionLifetime For(
        ISocketConnection connection,
        TimeProvider time,
        ILogger logger
    ) => All.GetValue(connection, c => new SocketConnectionLifetime(c, time, logger));

    /// <summary>
    /// Closes the connection at <paramref name="at"/> with <paramref name="reason"/>, unless an
    /// earlier deadline is already set. A deadline in the past closes it now.
    /// </summary>
    public void CloseAt(DateTimeOffset at, ConnectionCloseReason reason, string message)
    {
        lock (_lock)
        {
            if (_deadline is { } current && current <= at)
                return;

            _deadline = at;
            _deadlineReason = reason;
            _deadlineMessage = message;
            ScheduleLocked();
        }
    }

    /// <summary>
    /// Runs <paramref name="stillValid"/> every <paramref name="interval"/>, and closes the
    /// connection when it returns <c>false</c> or throws.
    /// </summary>
    public void RecheckEvery(TimeSpan interval, Func<CancellationToken, ValueTask<bool>> stillValid)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        lock (_lock)
        {
            _recheck = stillValid;
            _recheckInterval = interval;
            _nextRecheck = Add(_time.GetUtcNow(), interval);
            ScheduleLocked();
        }
    }

    /// <summary><paramref name="at"/> plus <paramref name="span"/>, held at the largest date.</summary>
    internal static DateTimeOffset Add(DateTimeOffset at, TimeSpan span) =>
        DateTimeOffset.MaxValue - at <= span ? DateTimeOffset.MaxValue : at + span;

    private void ScheduleLocked()
    {
        var due = _deadline ?? DateTimeOffset.MaxValue;
        if (_recheck is not null && !_rechecking && _nextRecheck < due)
            due = _nextRecheck;

        var wait = Math.Clamp((due - _time.GetUtcNow()).Ticks, 0, MaxTimerDue.Ticks);
        _timer.Change(TimeSpan.FromTicks(wait), Timeout.InfiniteTimeSpan);
    }

    private void OnTick()
    {
        Func<CancellationToken, ValueTask<bool>>? recheck = null;
        lock (_lock)
        {
            if (_closed)
                return;

            var now = _time.GetUtcNow();
            if (_deadline is { } deadline && deadline <= now)
            {
                _ = CloseLockedAsync(_deadlineReason, _deadlineMessage);
                return;
            }

            if (_recheck is not null && !_rechecking && _nextRecheck <= now)
            {
                _rechecking = true;
                recheck = _recheck;
            }

            // Armed for the deadline even while a re-check runs, so a resolver that never answers
            // cannot hold the connection open past it.
            ScheduleLocked();
        }

        if (recheck is not null)
            _ = RecheckAsync(recheck);
    }

    private async Task RecheckAsync(Func<CancellationToken, ValueTask<bool>> recheck)
    {
        bool valid;
        try
        {
            valid = await recheck(_connection.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Re-checking a subscription connection's credential failed; closing it."
            );
            valid = false;
        }

        lock (_lock)
        {
            _rechecking = false;
            if (_closed)
                return;

            if (!valid)
            {
                _ = CloseLockedAsync(ConnectionCloseReason.PolicyViolation, RevokedMessage);
                return;
            }

            _nextRecheck = Add(_time.GetUtcNow(), _recheckInterval);
            ScheduleLocked();
        }
    }

    private async Task CloseLockedAsync(ConnectionCloseReason reason, string message)
    {
        _closed = true;
        _timer.Dispose();
        All.Remove(_connection);
        try
        {
            await _connection.CloseAsync(message, reason, default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Closing a subscription connection at its end failed.");
        }
    }
}
