using System.Threading.Channels;
using Npgsql;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// A raw <c>LISTEN</c> on the snapshot change channel, on a connection of its own, the way an operator or a
/// second host would hear it. <see cref="Collect"/> returns every payload heard up to a marker the test sends on a
/// channel of its own: Postgres delivers notifications in commit order, so once the marker arrives every notice
/// from a transaction committed before it has arrived too. No test waits for a notice that may never come.
/// </summary>
internal sealed class SnapshotNotices : IAsyncDisposable
{
    /// <summary>The channel the listener hears, and so the one the migration must notify.</summary>
    public const string Channel = ISnapshotChangeListener.Channel;

    private const string MarkerChannel = "trax_test_notice_marker";

    /// <summary>How long a test waits for its own marker before it fails.</summary>
    private static readonly TimeSpan MarkerWait = TimeSpan.FromSeconds(15);

    private readonly NpgsqlConnection _connection;
    private readonly Channel<(string Channel, string Payload)> _heard =
        System.Threading.Channels.Channel.CreateUnbounded<(string, string)>();
    private readonly CancellationTokenSource _stop = new();
    private Task _pump = Task.CompletedTask;

    private readonly string _connectionString;

    private SnapshotNotices(NpgsqlConnection connection, string connectionString)
    {
        _connection = connection;
        _connectionString = connectionString;
        _connection.Notification += (_, args) =>
            _heard.Writer.TryWrite((args.Channel, args.Payload));
    }

    /// <summary>
    /// Opens a connection (to the run's database, or <paramref name="connectionString"/>) and listens on the snapshot
    /// channel and the marker channel.
    /// </summary>
    public static async Task<SnapshotNotices> Listen(string? connectionString = null)
    {
        connectionString ??= PostgresSetup.ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var notices = new SnapshotNotices(connection, connectionString);
        await using (var listen = connection.CreateCommand())
        {
            listen.CommandText = $"LISTEN {Channel}; LISTEN {MarkerChannel}";
            await listen.ExecuteNonQueryAsync();
        }
        notices._pump = notices.Pump();
        return notices;
    }

    /// <summary>Forgets everything heard so far.</summary>
    public async Task Drain() => await Collect();

    /// <summary>
    /// Every snapshot-channel payload heard since the last collect, up to a marker sent now. Fails when the marker
    /// does not arrive within the bounded wait.
    /// </summary>
    public async Task<IReadOnlyList<string>> Collect()
    {
        var marker = Guid.NewGuid().ToString("N");
        await Notify(MarkerChannel, marker, _connectionString);

        using var timeout = new CancellationTokenSource(MarkerWait);
        var payloads = new List<string>();
        try
        {
            while (true)
            {
                var (channel, payload) = await _heard.Reader.ReadAsync(timeout.Token);
                if (channel == MarkerChannel)
                {
                    if (payload == marker)
                        return payloads;
                    continue;
                }
                payloads.Add(payload);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The test's own marker notice was not heard within {MarkerWait}."
            );
        }
    }

    /// <summary>
    /// Sends <c>NOTIFY</c> on <paramref name="channel"/> from a connection of its own, to the run's database or
    /// <paramref name="connectionString"/>.
    /// </summary>
    public static async Task Notify(string channel, string payload, string? connectionString = null)
    {
        await using var connection = new NpgsqlConnection(
            connectionString ?? PostgresSetup.ConnectionString
        );
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_notify(@channel, @payload)";
        command.Parameters.AddWithValue("@channel", channel);
        command.Parameters.AddWithValue("@payload", payload);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Pump()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
                await _connection.WaitAsync(_stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _pump;
        await _connection.DisposeAsync();
        _stop.Dispose();
    }
}
