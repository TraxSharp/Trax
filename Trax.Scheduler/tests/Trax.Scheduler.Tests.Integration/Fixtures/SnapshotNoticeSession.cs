using System.Collections.Concurrent;
using Npgsql;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// The test's own Postgres session listening on <c>trax_snapshot_changed</c>, the channel a committed write to the
/// snapshot table announces its row on. Once a query on the session returns, every notice committed before that
/// query began has reached it, so <see cref="Drain"/> reads an exact count without waiting on a clock.
/// </summary>
internal sealed class SnapshotNoticeSession : IAsyncDisposable
{
    public const string Channel = "trax_snapshot_changed";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly NpgsqlConnection _connection;
    private readonly ConcurrentQueue<string> _payloads = new();

    private SnapshotNoticeSession(NpgsqlConnection connection)
    {
        _connection = connection;
        _connection.Notification += (_, e) =>
        {
            if (string.Equals(e.Channel, Channel, StringComparison.Ordinal))
                _payloads.Enqueue(e.Payload);
        };
    }

    public static async Task<SnapshotNoticeSession> Open(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var listener = new SnapshotNoticeSession(connection);
        var listen = new NpgsqlCommand($"LISTEN {Channel}", connection);
        await using (listen.ConfigureAwait(false))
        {
            await listen.ExecuteNonQueryAsync().ConfigureAwait(false);
            return listener;
        }
    }

    /// <summary>
    /// Every payload heard since the last drain, after a round trip that brings in all those committed before it.
    /// </summary>
    public async Task<IReadOnlyList<string>> Drain()
    {
        using var bound = new CancellationTokenSource(Bound);
        var roundTrip = new NpgsqlCommand("SELECT 1", _connection);
        await using (roundTrip.ConfigureAwait(false))
        {
            await roundTrip.ExecuteScalarAsync(bound.Token).ConfigureAwait(false);
            var drained = new List<string>();
            while (_payloads.TryDequeue(out var payload))
                drained.Add(payload);
            return drained;
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
