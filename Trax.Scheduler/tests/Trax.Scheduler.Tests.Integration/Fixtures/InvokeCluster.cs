using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Data.Testing;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// Several hosts over one database of their own, as a deployment has them: API hosts that register the machines
/// (and so the outcome hook and reconciler), and scheduler hosts that dispatch and run the trains, with or without
/// the machines. Nothing runs in the background: a test dispatches, runs, reaps and sweeps by calling each step,
/// so every wait is on a step the test started.
/// </summary>
/// <remarks>
/// On Postgres the database is created for this process and cluster (<c>trax_invoke_delivery_{pid}_{n}</c>) and
/// dropped at the end, so another run sharing the server never touches it; on SQLite it is a file of its own.
/// </remarks>
public sealed class InvokeCluster : IAsyncDisposable
{
    private static int _clusters;

    private static readonly ConcurrentDictionary<InvokeCluster, byte> Open = new();

    private readonly List<ClusterHost> _hosts = [];
    private readonly ConcurrentDictionary<InvokingState, byte> _invokingStates = new();
    private readonly string? _sqliteFile;
    private readonly string? _database;

    private InvokeCluster(
        ClusterStore store,
        string connectionString,
        string? sqliteFile,
        string? database
    )
    {
        Store = store;
        ConnectionString = connectionString;
        _sqliteFile = sqliteFile;
        _database = database;
    }

    public ClusterStore Store { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// The clusters created and not yet disposed, on either store, whose databases
    /// <see cref="CheckTraxInvariantsAttribute"/> checks after each test.
    /// </summary>
    internal static IReadOnlyList<InvokeCluster> OpenClusters => Open.Keys.ToList();

    /// <summary>Every state that invokes a train, of the machines this cluster's hosts register.</summary>
    internal IReadOnlyList<InvokingState> InvokingStates => _invokingStates.Keys.ToList();

    public static async Task<InvokeCluster> Create(ClusterStore store)
    {
        if (store == ClusterStore.Sqlite)
        {
            var file = Path.Combine(
                Path.GetTempPath(),
                $"trax_invoke_delivery_{Guid.NewGuid():N}.db"
            );
            var onSqlite = new InvokeCluster(store, $"Data Source={file}", file, database: null);
            Open[onSqlite] = 0;
            return onSqlite;
        }

        var database = string.Create(
            CultureInfo.InvariantCulture,
            $"trax_invoke_delivery_{Environment.ProcessId}_{Interlocked.Increment(ref _clusters)}"
        );
        var admin = new NpgsqlConnection(TestPostgres.ConnectionStringFor("postgres"));
        await using (admin.ConfigureAwait(false))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)")
                .ConfigureAwait(false);
            await Exec(admin, $"CREATE DATABASE {database}").ConfigureAwait(false);
        }

        var cluster = new InvokeCluster(
            store,
            TestPostgres.ConnectionStringFor(database),
            sqliteFile: null,
            database
        );
        Open[cluster] = 0;
        return cluster;
    }

    /// <summary>
    /// A host. <paramref name="machines"/> registers the state machines (their outcome hook and reconciler);
    /// <paramref name="scheduler"/> registers the scheduler, whose dispatcher hands runs to this host's
    /// <see cref="ClusterHost.RunJob"/> instead of a worker, configured further by <paramref name="scheduling"/>.
    /// <paramref name="data"/> adds effects over the data provider, such as junction events or decision recording.
    /// </summary>
    public ClusterHost Host(
        bool machines,
        bool scheduler,
        Action<IServiceCollection>? configure = null,
        Action<StateMachineOptions>? options = null,
        Action<SchedulerConfigurationBuilder>? scheduling = null,
        Func<TraxEffectBuilderWithData, TraxEffectBuilderWithData>? data = null
    )
    {
        var held = new HeldJobs();
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(held);
        services.AddTrax(trax =>
        {
            var effects = trax.AddEffects(e =>
            {
                var withData =
                    Store == ClusterStore.Postgres
                        ? e.UsePostgres(ConnectionString)
                        : e.UseSqlite(ConnectionString);
                return (data?.Invoke(withData) ?? withData).AddJunctionProgress();
            });
            var withMachines = machines
                ? effects.AddStateMachines(options, typeof(StepMachine).Assembly)
                : effects;
            var mediator = withMachines.AddMediator(
                typeof(AssemblyMarker).Assembly,
                typeof(JobRunnerTrain).Assembly
            );
            if (scheduler)
                mediator.AddScheduler(s =>
                {
                    s.OverrideSubmitter(x => x.AddScoped<IJobSubmitter, HeldSubmitter>());
                    scheduling?.Invoke(s);
                    return s;
                });
        });
        configure?.Invoke(services);

        var host = new ClusterHost(services.BuildServiceProvider(), held);
        _hosts.Add(host);

        // Read now, while the host is up: a test may dispose a host before the cluster's last check.
        foreach (var machine in host.Services.GetServices<IMachine>().Cast<IMachineInternals>())
        foreach (var invoked in machine.InvokedTrains)
            _invokingStates[new InvokingState(invoked.Machine, invoked.State)] = 0;
        return host;
    }

    /// <summary>Empties every table a test writes, so each test starts from nothing.</summary>
    public async Task Reset()
    {
        var host = _hosts.FirstOrDefault() ?? Host(machines: false, scheduler: false);
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await context.SnapshotDrafts.ExecuteDeleteAsync().ConfigureAwait(false);
        await TestSetup.CleanupDatabase(context).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Open.TryRemove(this, out _);

        foreach (var host in _hosts)
            await host.DisposeAsync().ConfigureAwait(false);

        if (_sqliteFile is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try
                {
                    File.Delete(_sqliteFile + suffix);
                }
                catch (IOException)
                {
                    // Best effort: a lingering handle can hold the temp file.
                }
        }

        if (_database is not null)
        {
            NpgsqlConnection.ClearAllPools();
            var admin = new NpgsqlConnection(TestPostgres.ConnectionStringFor("postgres"));
            await using (admin.ConfigureAwait(false))
            {
                await admin.OpenAsync().ConfigureAwait(false);
                await Exec(admin, $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)")
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
