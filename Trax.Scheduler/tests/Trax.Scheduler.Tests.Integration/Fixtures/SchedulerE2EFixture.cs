using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// Per-test scaffold that boots a full Trax scheduler with Postgres + the test-train assembly,
/// lets the test customise the SchedulerConfigurationBuilder, materialises the queued
/// PendingManifests via SchedulerStartupService's seeding path, and exposes the orchestration
/// trains (ManifestManager, JobDispatcher) so tests can drive the polling cycle directly.
/// </summary>
/// <remarks>
/// Distinct from <see cref="TestSetup"/>, which uses a single shared ServiceProvider
/// with a fixed scheduler config (no custom Schedule / Include / ScheduleMany). The E2E
/// fixture builds a fresh provider per test so each test can declare its own scheduling
/// graph and assert on the resulting Manifest / WorkQueue / Metadata rows.
/// </remarks>
public sealed class SchedulerE2EFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public IDataContext DataContext { get; }
    public ITraxScheduler Scheduler { get; }
    public SchedulerConfiguration Configuration { get; }

    /// <summary>The scoped service provider, so tests can resolve additional services (e.g. IOperationsService).</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    private SchedulerE2EFixture(
        ServiceProvider provider,
        IServiceScope scope,
        IDataContext dataContext,
        ITraxScheduler scheduler,
        SchedulerConfiguration configuration
    )
    {
        _provider = provider;
        _scope = scope;
        DataContext = dataContext;
        Scheduler = scheduler;
        Configuration = configuration;
    }

    /// <summary>
    /// Builds a fresh ServiceProvider, applies <paramref name="configureScheduler"/>, cleans
    /// the database, and returns a fixture wired up for the test. Disposing the fixture tears
    /// down the provider.
    /// </summary>
    public static async Task<SchedulerE2EFixture> CreateAsync(
        Action<SchedulerConfigurationBuilder> configureScheduler,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

        // Each E2E test stands up its own ServiceProvider with its own Npgsql connection pool.
        // Pin the pool to a single connection that immediately returns to the pool — without
        // this, parallel fixtures briefly hold dozens of connections and trip Postgres's
        // max_connections cap (the validation tests in this suite see "53300: too many clients").
        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
            connectionString +=
                ";Pooling=true;Maximum Pool Size=1;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(NullLoggerProvider.Instance))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects.UsePostgres(connectionString).AddJson().SaveTrainParameters()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        scheduler.UseInMemoryWorkers();
                        configureScheduler(scheduler);
                        return scheduler;
                    })
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            });

        configureServices?.Invoke(services);
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        await TestSetup.CleanupDatabase(dataContext);

        return new SchedulerE2EFixture(
            provider,
            scope,
            dataContext,
            scope.ServiceProvider.GetRequiredService<ITraxScheduler>(),
            scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>()
        );
    }

    /// <summary>
    /// Invokes every queued <c>PendingManifest.ScheduleFunc</c> against the live scheduler.
    /// Mirrors what <c>SchedulerStartupService.SeedPendingManifests</c> does at host startup,
    /// but in the test thread so failures surface as exceptions rather than logged warnings.
    /// </summary>
    public async Task MaterializePendingManifestsAsync(CancellationToken ct = default)
    {
        foreach (var pending in Configuration.PendingManifests.ToList())
            await pending.ScheduleFunc(Scheduler, ct);
    }

    /// <summary>
    /// Runs the ManifestManager train end-to-end: loads manifests, processes timeouts,
    /// determines which jobs to queue, and writes WorkQueue entries. Use after
    /// <see cref="MaterializePendingManifestsAsync"/> to exercise the queueing pipeline.
    /// </summary>
    public Task RunManifestManagerAsync(CancellationToken ct = default)
    {
        var train = _scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        return train.Run(LanguageExt.Unit.Default, ct);
    }

    /// <summary>
    /// Runs the JobDispatcher train end-to-end: loads queued work, applies capacity limits,
    /// and dispatches jobs to the registered run executor.
    /// </summary>
    public Task RunJobDispatcherAsync(CancellationToken ct = default)
    {
        var train = _scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        return train.Run(LanguageExt.Unit.Default, ct);
    }

    /// <summary>
    /// Runs the DeadLetterCleanup train end-to-end: deletes resolved dead letters older than
    /// the configured retention period.
    /// </summary>
    public Task RunDeadLetterCleanupAsync(CancellationToken ct = default)
    {
        var train = _scope.ServiceProvider.GetRequiredService<IDeadLetterCleanupTrain>();
        return train.Run(new DeadLetterCleanupRequest(), ct);
    }

    /// <summary>
    /// Builds a fresh provider with the InMemory data provider so the test exercises
    /// InMemoryManifestManagerTrain + InMemoryDispatchJobsJunction (the path used by tests
    /// and small samples that don't need Postgres). Each call gets its own EF Core
    /// InMemory database so no cleanup is needed.
    /// </summary>
    public static SchedulerE2EFixture CreateInMemory(
        Action<SchedulerConfigurationBuilder> configureScheduler,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(NullLoggerProvider.Instance))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddJson().SaveTrainParameters())
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        configureScheduler(scheduler);
                        return scheduler;
                    })
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            });

        configureServices?.Invoke(services);
        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        return new SchedulerE2EFixture(
            provider,
            scope,
            dataContext,
            scope.ServiceProvider.GetRequiredService<ITraxScheduler>(),
            scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>()
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (DataContext is IAsyncDisposable async)
            await async.DisposeAsync();
        else if (DataContext is IDisposable sync)
            sync.Dispose();

        _scope.Dispose();
        await _provider.DisposeAsync();

        // Aggressively clear Npgsql connection pools so the next test in the suite
        // (which builds its own ServiceProvider with its own pool) doesn't hit the
        // server-side max_connections cap on the shared trax_scheduler_tests database.
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
