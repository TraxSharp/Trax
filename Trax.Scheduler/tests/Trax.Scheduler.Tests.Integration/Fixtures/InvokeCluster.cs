using System.Collections.Concurrent;
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
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>The stores the invoked-train delivery tests run on.</summary>
public enum ClusterStore
{
    Postgres,
    Sqlite,
}

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

    private readonly List<ClusterHost> _hosts = [];
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

    public static async Task<InvokeCluster> Create(ClusterStore store)
    {
        if (store == ClusterStore.Sqlite)
        {
            var file = Path.Combine(
                Path.GetTempPath(),
                $"trax_invoke_delivery_{Guid.NewGuid():N}.db"
            );
            return new InvokeCluster(store, $"Data Source={file}", file, null);
        }

        var database =
            $"trax_invoke_delivery_{Environment.ProcessId}_{Interlocked.Increment(ref _clusters)}";
        await using (var admin = new NpgsqlConnection(TestPostgres.ConnectionStringFor("postgres")))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {database}");
        }

        return new InvokeCluster(store, TestPostgres.ConnectionStringFor(database), null, database);
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

        var host = new ClusterHost(services.BuildServiceProvider(), held, Store);
        _hosts.Add(host);
        return host;
    }

    /// <summary>Empties every table a test writes, so each test starts from nothing.</summary>
    public async Task Reset()
    {
        var host = _hosts.FirstOrDefault() ?? Host(machines: false, scheduler: false);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await context.SnapshotDrafts.ExecuteDeleteAsync();
        await TestSetup.CleanupDatabase(context);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();

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
            await using var admin = new NpgsqlConnection(
                TestPostgres.ConnectionStringFor("postgres")
            );
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)");
        }
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>The runs a scheduler host's dispatcher handed over, by metadata id, with their inputs.</summary>
public sealed class HeldJobs
{
    public ConcurrentDictionary<long, object?> Inputs { get; } = new();
}

/// <summary>A submitter that holds each dispatched run for the test to run, on the host it chooses.</summary>
public sealed class HeldSubmitter(HeldJobs held) : IJobSubmitter
{
    public Task<string> EnqueueAsync(long metadataId)
    {
        held.Inputs[metadataId] = null;
        return Task.FromResult($"held-{metadataId}");
    }

    public Task<string> EnqueueAsync(long metadataId, object input)
    {
        held.Inputs[metadataId] = input;
        return Task.FromResult($"held-{metadataId}");
    }
}

/// <summary>One host of an <see cref="InvokeCluster"/>.</summary>
public sealed class ClusterHost(ServiceProvider services, HeldJobs held, ClusterStore store)
    : IAsyncDisposable
{
    public ServiceProvider Services => services;

    internal InvokeOutcomeReconciler Reconciler =>
        services.GetRequiredService<InvokeOutcomeReconciler>();

    /// <summary>Starts a system-owned instance whose initial state invokes the step, with <paramref name="context"/>.</summary>
    public async Task<MachineInstance> Start(JsonObject context)
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<SystemStepMachine>(
                MachineKey.Of("delivery", Guid.NewGuid().ToString("N")),
                context
            );
    }

    /// <summary>Saves <paramref name="user"/>'s draft in <c>Idle</c> with <paramref name="context"/> and advances it into <c>Running</c>.</summary>
    public async Task<AdvanceOutcome> EnterRunning(string user, Guid id, JsonObject context)
    {
        using var scope = services.CreateScope();
        var drafts = Drafts(scope);
        var saved = await drafts.Autosave(
            user,
            id,
            StepMachine.Json(UserStepMachine.MachineId, "Idle", context)
        );
        if (saved is not AutosaveResult.Saved)
            throw new InvalidOperationException($"The draft could not be saved: {saved}");
        return await drafts.Advance(user, id, nameof(StepTrigger.Go));
    }

    public async Task<AdvanceOutcome> Advance(string user, Guid id, StepTrigger trigger)
    {
        using var scope = services.CreateScope();
        return await Drafts(scope).Advance(user, id, trigger.ToString());
    }

    private static ISnapshotDraftService Drafts(IServiceScope scope) =>
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(UserStepMachine.MachineId)!;

    internal Task<IReadOnlyList<InvokeDelivery>> Sweep() =>
        Reconciler.SweepOnce(CancellationToken.None);

    internal Task<InvokeDelivery?> Deliver(string token) =>
        Reconciler.TryDeliver(token, CancellationToken.None);

    /// <summary>Runs the dispatcher once and returns the run it created for <paramref name="token"/>'s entry.</summary>
    public async Task<long> Dispatch(string token)
    {
        using (var scope = services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        var entry = await Entry(token);
        return entry?.MetadataId
            ?? throw new InvalidOperationException(
                $"The entry {token} was not dispatched: it is {entry?.Status.ToString() ?? "missing"}."
            );
    }

    /// <summary>
    /// Runs the dispatched run <paramref name="metadataId"/> here, as a worker would, to its end. A run that fails
    /// or is cancelled records that on its row, which is what a test reads; the runner's rethrow is not.
    /// </summary>
    public async Task RunJob(long metadataId)
    {
        if (!held.Inputs.TryGetValue(metadataId, out var input))
            throw new InvalidOperationException(
                $"Run {metadataId} was not dispatched to this host."
            );

        using var scope = services.CreateScope();
        try
        {
            await scope
                .ServiceProvider.GetRequiredService<IJobRunnerTrain>()
                .Run(new RunJobRequest(metadataId, input));
        }
        catch (Exception)
        {
            // Recorded on the run's row.
        }
    }

    /// <summary>The input this host's dispatcher handed over with the run <paramref name="metadataId"/>.</summary>
    public object? HeldInput(long metadataId) =>
        held.Inputs.TryGetValue(metadataId, out var input)
            ? input
            : throw new InvalidOperationException(
                $"Run {metadataId} was not dispatched to this host."
            );

    /// <summary>Dispatches <paramref name="token"/>'s entry and runs it here to its end.</summary>
    public async Task<long> DispatchAndRun(string token)
    {
        var id = await Dispatch(token);
        await RunJob(id);
        return id;
    }

    /// <summary>
    /// One manifest manager cycle: the timeouts, the reapers, and their lifecycle events once it commits. On
    /// Postgres through the polling service's leader path; on SQLite, which has no leader lock to take, the train
    /// runs in a scope of its own and its deferred events are flushed after it, as the polling service does.
    /// </summary>
    public async Task RunManifestManager()
    {
        if (store == ClusterStore.Sqlite)
        {
            using var scope = services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IManifestManagerTrain>()
                .Run(Unit.Default);
            await scope.ServiceProvider.GetRequiredService<DeferredOutcomeEvents>().FlushAsync();
            return;
        }

        var polling = new ManifestManagerPollingService(
            services,
            services.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            services.GetRequiredService<ISqlDialect>()
        );
        await polling.RunManifestManager(CancellationToken.None);
    }

    public async Task<SnapshotDraft?> Row(Guid id, string machine)
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.Machine == machine);
    }

    public async Task<WorkQueue?> Entry(string token)
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExternalId == token);
    }

    /// <summary>Every entry an instance's invoking states queued, oldest first.</summary>
    public async Task<List<WorkQueue>> Entries(Guid instance)
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(x => x.InvokingInstanceId == instance)
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    public async Task<Metadata> Run(long metadataId)
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .SingleAsync(x => x.Id == metadataId);
    }

    /// <summary>Moves a run's start back by <paramref name="age"/>, as if it had been going that long.</summary>
    public async Task Age(long metadataId, TimeSpan age)
    {
        using var scope = services.CreateScope();
        var start = DateTime.UtcNow - age;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.Where(x => x.Id == metadataId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, start));
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
