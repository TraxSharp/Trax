using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.QueuedWorkListener;

namespace Trax.Effect.Data.Postgres.Services.QueuedWorkListener;

/// <summary>
/// Hears the <c>NOTIFY</c> that migration 069's triggers on <c>trax.work_queue</c> send when an
/// entry is inserted or a staged one is confirmed, from any host, once the writing transaction
/// commits.
/// </summary>
/// <remarks>
/// Each subscription holds one connection for as long as it listens, so it comes from a data
/// source of its own with pooling off: it never takes a slot in the pool the data context draws
/// from, and a terminated connection is not handed to anything else. The data source is built
/// with the host's own configuration (a password provider, TLS callbacks) and then given a
/// keepalive, so a connection lost without a reset is noticed rather than waited on forever, and
/// an application name, so an operator can find the session in <c>pg_stat_activity</c>.
/// </remarks>
internal sealed class PostgresQueuedWorkListener(
    string connectionString,
    Action<NpgsqlDataSourceBuilder>? configureDataSource
) : IQueuedWorkListener, IAsyncDisposable
{
    /// <summary>The channel migration 069's trigger function notifies.</summary>
    internal const string Channel = "trax_queued_work";

    /// <summary>The <c>application_name</c> a listening session reports.</summary>
    internal const string ApplicationName = "trax_queued_work_listener";

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

    public async Task<IQueuedWorkSubscription> SubscribeAsync(CancellationToken cancellationToken)
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

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated)
            await _dataSource.Value.DisposeAsync();
    }

    private sealed class Subscription : IQueuedWorkSubscription
    {
        private readonly NpgsqlConnection _connection;
        private int _pending;

        public Subscription(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, args) =>
            {
                if (args.Channel == Channel)
                    Interlocked.Increment(ref _pending);
            };
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            // NpgsqlConnection.WaitAsync returns on any asynchronous message, a notice or a
            // parameter change included, and may deliver several notifications in one read. Only
            // the notifications count, one per call.
            while (Volatile.Read(ref _pending) == 0)
                await _connection.WaitAsync(cancellationToken);

            Interlocked.Decrement(ref _pending);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
