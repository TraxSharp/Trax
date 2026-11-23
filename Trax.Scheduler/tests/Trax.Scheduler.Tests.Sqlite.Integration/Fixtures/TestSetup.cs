using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider
    {
        get => field.NotNull(nameof(ServiceProvider));
        set;
    }
    private string? _dbPath;

    public AsyncServiceScope Scope { get; private set; }

    public ITrainBus TrainBus
    {
        get => field.NotNull(nameof(TrainBus));
        private set;
    }

    public IJobRunnerTrain JobRunner
    {
        get => field.NotNull(nameof(JobRunner));
        private set;
    }

    public IDataContext DataContext
    {
        get => field.NotNull(nameof(DataContext));
        private set;
    }

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_scheduler_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath.NotNull()}";

        var arrayLoggingProvider = new ArrayLoggingProvider();

        ServiceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Information)
                            .SaveTrainParameters()
                            .UseSqlite(connectionString)
                            .AddDataContextLogging(minimumLogLevel: LogLevel.Trace)
                            .AddJson()
                            .AddJunctionLogger(serializeJunctionData: true)
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers().AddMetadataCleanup())
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await ServiceProvider.DisposeAsync();

        if (File.Exists(_dbPath.NotNull()))
            File.Delete(_dbPath.NotNull());
        var walPath = _dbPath.NotNull() + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);
        var shmPath = _dbPath.NotNull() + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    [SetUp]
    public virtual async Task TestSetUp()
    {
        Scope = ServiceProvider.CreateAsyncScope();
        TrainBus = Scope.ServiceProvider.GetRequiredService<ITrainBus>();
        JobRunner = Scope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();
        DataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();

        await CleanupDatabase(DataContext);
    }

    public static async Task CleanupDatabase(IDataContext dataContext)
    {
        await dataContext.BackgroundJobs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Logs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.WorkQueues.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.DeadLetters.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Metadatas.ExecuteDeleteAsync().ConfigureAwait(false);

        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null))
            .ConfigureAwait(false);
        await dataContext.Manifests.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.ManifestGroups.ExecuteDeleteAsync().ConfigureAwait(false);

        dataContext.Reset();
    }

    public static async Task<ManifestGroup> CreateAndSaveManifestGroup(
        IDataContext dataContext,
        string name = "test-group",
        int? maxActiveJobs = null,
        int priority = 0,
        bool isEnabled = true
    )
    {
        var group = new ManifestGroup
        {
            Name = name,
            MaxActiveJobs = maxActiveJobs,
            Priority = priority,
            IsEnabled = isEnabled,
            CreatedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
            UpdatedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
        };
        await dataContext.Track(group).ConfigureAwait(false);
        await dataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        dataContext.Reset();
        return group;
    }

    [TearDown]
    public async Task TestTearDown()
    {
        if (JobRunner is IDisposable jobRunnerDisposable)
            jobRunnerDisposable.Dispose();

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        await Scope.DisposeAsync().ConfigureAwait(false);
    }
}
