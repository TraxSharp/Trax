using System.Collections.Concurrent;
using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.InvokedRunListener;

namespace Trax.Effect.Data.Postgres.Services.InvokedRunListener;

/// <summary>
/// Hears the <c>NOTIFY</c> that migration 073's triggers send when a run a state machine invoked ends, from any
/// host, once the writing transaction commits. Each notice carries the run's external id.
/// </summary>
/// <remarks>
/// Built like <c>PostgresQueuedWorkListener</c>: each subscription holds one connection from a data source of its
/// own with pooling off, configured as the host configured its own, with a keepalive and an application name an
/// operator can find in <c>pg_stat_activity</c>.
/// </remarks>
internal sealed class PostgresInvokedRunListener(
    string connectionString,
    Action<NpgsqlDataSourceBuilder>? configureDataSource
) : IInvokedRunListener, IAsyncDisposable
{
    /// <summary>The channel migration 073's trigger function notifies.</summary>
    internal const string Channel = "trax_invoked_run_ended";

    /// <summary>The <c>application_name</c> a listening session reports.</summary>
    internal const string ApplicationName = "trax_invoked_run_listener";

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

    public async Task<IInvokedRunSubscription> SubscribeAsync(CancellationToken cancellationToken)
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

    private sealed class Subscription : IInvokedRunSubscription
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

        public async Task<string> NextAsync(CancellationToken cancellationToken)
        {
            // NpgsqlConnection.WaitAsync returns on any asynchronous message and may deliver several
            // notifications in one read; each is handed out once.
            string? next;
            while (!_pending.TryDequeue(out next))
                await _connection.WaitAsync(cancellationToken);

            return next;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
