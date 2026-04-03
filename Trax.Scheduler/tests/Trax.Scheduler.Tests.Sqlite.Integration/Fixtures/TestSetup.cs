using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private ServiceProvider ServiceProvider { get; set; } = null!;
    private string _dbPath = null!;

    public IServiceScope Scope { get; private set; } = null!;

    public ITrainBus TrainBus { get; private set; } = null!;

    public IJobRunnerTrain JobRunner { get; private set; } = null!;

    public IDataContext DataContext { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_scheduler_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

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

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    [SetUp]
    public virtual async Task TestSetUp()
    {
        Scope = ServiceProvider.CreateScope();
        TrainBus = Scope.ServiceProvider.GetRequiredService<ITrainBus>();
        JobRunner = Scope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();
        DataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();

        await CleanupDatabase(DataContext);
    }

    public static async Task CleanupDatabase(IDataContext dataContext)
    {
        await dataContext.BackgroundJobs.ExecuteDeleteAsync();
        await dataContext.Logs.ExecuteDeleteAsync();
        await dataContext.WorkQueues.ExecuteDeleteAsync();
        await dataContext.DeadLetters.ExecuteDeleteAsync();
        await dataContext.Metadatas.ExecuteDeleteAsync();

        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null));
        await dataContext.Manifests.ExecuteDeleteAsync();
        await dataContext.ManifestGroups.ExecuteDeleteAsync();

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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await dataContext.Track(group);
        await dataContext.SaveChanges(CancellationToken.None);
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

        Scope.Dispose();
    }
}
