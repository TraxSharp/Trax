using System.Collections.Concurrent;
using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.SnapshotNotices;

namespace Trax.Effect.Data.Postgres.Services.SnapshotNotices;

/// <summary>
/// Hears the <c>NOTIFY</c> that migration 078's trigger sends for every committed write to
/// <c>trax.snapshot_draft</c>, from any host, unless <c>trax.snapshot_notify</c> is <c>'off'</c> (in any case) for
/// the writing session (migration 080). Each notice carries the row's <c>row_id</c>.
/// </summary>
/// <remarks>
/// Built like <c>PostgresInvokedRunListener</c>: each subscription holds one connection from a data source of its
/// own with pooling off, configured as the host configured its own, with a keepalive and an application name an
/// operator can find in <c>pg_stat_activity</c>. The session only ever runs <c>LISTEN</c> and a one-row
/// <c>SELECT</c> in autocommit, so it never sits idle in a transaction.
/// </remarks>
internal sealed class PostgresSnapshotNoticeSource(
    string connectionString,
    Action<NpgsqlDataSourceBuilder>? configureDataSource
) : ISnapshotNoticeSource, IAsyncDisposable, IDisposable
{
    /// <summary>The channel migration 078's trigger function notifies.</summary>
    internal const string Channel = "trax_snapshot_changed";

    /// <summary>The <c>application_name</c> a listening session reports.</summary>
    internal const string ApplicationName = "trax_snapshot_listener";

    private const int KeepAliveSeconds = 30;

    private readonly Lazy<NpgsqlDataSource> _dataSource = new(
        () =>
            ModelBuilderExtensions.BuildDataSource(
                connectionString,
                builder =>
                {
                    configureDataSource?.Invoke(builder);
                    var settings = builder.ConnectionStringBuilder;
                    settings.Pooling = false;
                    settings.Multiplexing = false;
                    settings.ApplicationName = ApplicationName;
                    if (settings.KeepAlive == 0)
                        settings.KeepAlive = KeepAliveSeconds;
                }
            ),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    public async Task<ISnapshotNoticeSubscription> SubscribeAsync(
        CancellationToken cancellationToken
    )
    {
        var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        try
        {
            var subscription = new Subscription(connection);
            await using var listen = new NpgsqlCommand($"LISTEN {Channel}", connection);
            await listen.ExecuteNonQueryAsync(cancellationToken);
            return subscription;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<SnapshotNoticeSwitch> ReadSwitchAsync(CancellationToken cancellationToken)
    {
        // The database's own value is its pg_db_role_setting row with no role. A role's value is the one it sets in
        // this database, else the one it sets everywhere; only the roles whose value is off are named. The session's
        // value and replication role are this host's, read on a new session with the host's connection string.
        await using var connection = await _dataSource.Value.OpenConnectionAsync(cancellationToken);
        await using var read = new NpgsqlCommand(
            """
            WITH settings AS (
                SELECT s.setdatabase, s.setrole, substr(c, strpos(c, '=') + 1) AS value
                FROM pg_db_role_setting s, unnest(s.setconfig) AS c
                WHERE s.setdatabase IN (0, (SELECT oid FROM pg_database WHERE datname = current_database()))
                  AND lower(split_part(c, '=', 1)) = 'trax.snapshot_notify'
            ),
            roles AS (
                SELECT DISTINCT ON (setrole) setrole, value
                FROM settings
                WHERE setrole <> 0
                ORDER BY setrole, setdatabase DESC
            ),
            trg AS (
                SELECT tgenabled::text AS state, tgqual IS NOT NULL AS has_condition
                FROM pg_trigger
                WHERE tgname = 'snapshot_draft_changed_notify'
                  AND tgrelid = to_regclass('trax.snapshot_draft')
            )
            SELECT
                (SELECT state FROM trg),
                COALESCE((SELECT has_condition FROM trg), false),
                (SELECT value FROM settings WHERE setrole = 0 AND setdatabase <> 0),
                current_setting('trax.snapshot_notify', true),
                current_setting('session_replication_role') = 'replica',
                ARRAY(
                    SELECT r.rolname::text
                    FROM roles JOIN pg_roles r ON r.oid = roles.setrole
                    WHERE lower(roles.value) = 'off'
                    ORDER BY 1)
            """,
            connection
        );
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SnapshotNoticeSwitch(
            await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0),
            reader.GetBoolean(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
            await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetFieldValue<string[]>(5)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated)
            await _dataSource.Value.DisposeAsync();
    }

    public void Dispose()
    {
        if (_dataSource.IsValueCreated)
            _dataSource.Value.Dispose();
    }

    private sealed class Subscription : ISnapshotNoticeSubscription
    {
        private readonly NpgsqlConnection _connection;
        private readonly ConcurrentQueue<string> _pending = new();

        public Subscription(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, args) =>
            {
                if (args.Channel == Channel)
                    _pending.Enqueue(args.Payload);
            };
        }

        public async Task<string?> NextAsync(TimeSpan idle, CancellationToken cancellationToken)
        {
            // NpgsqlConnection.WaitAsync returns on any asynchronous message and may deliver several
            // notifications in one read; each is handed out once. A timeout leaves the connection usable.
            string? next;
            while (!_pending.TryDequeue(out next))
                if (!await _connection.WaitAsync(idle, cancellationToken))
                    return _pending.TryDequeue(out next) ? next : null;

            return next;
        }

        public async Task<double> QueueUsageAsync(CancellationToken cancellationToken)
        {
            await using var usage = new NpgsqlCommand(
                "SELECT pg_notification_queue_usage()",
                _connection
            );
            return Convert.ToDouble(
                await usage.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture
            );
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
