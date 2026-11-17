using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
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

namespace Trax.Scheduler.Tests.Integration.Fixtures;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider
    {
        get => field.NotNull(nameof(ServiceProvider));
        set;
    }

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
        var connectionString = TestPostgres.ConnectionString;

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
                            .UsePostgres(connectionString)
                            .AddDataContextLogging(minimumLogLevel: LogLevel.Trace)
                            .AddJson()
                            .AddJunctionLogger(serializeJunctionData: true)
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers().AddMetadataCleanup())
            )
            // Register IDataContext as scoped, created from the factory
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

    /// <summary>
    /// Deletes all rows from all scheduler tables in FK-safe order to ensure
    /// complete test isolation between runs.
    /// </summary>
    public static async Task CleanupDatabase(IDataContext dataContext)
    {
        // Delete in FK-safe order (children before parents)
        await dataContext.BackgroundJobs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Logs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.WorkQueues.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.DeadLetters.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Metadatas.ExecuteDeleteAsync().ConfigureAwait(false);

        // Clear self-referencing FK before deleting manifests
        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null))
            .ConfigureAwait(false);
        await dataContext.Manifests.ExecuteDeleteAsync().ConfigureAwait(false);

        // Delete manifest groups after manifests (FK dependency)
        await dataContext.ManifestGroups.ExecuteDeleteAsync().ConfigureAwait(false);

        dataContext.Reset();
    }

    /// <summary>
    /// Creates and persists a ManifestGroup for test use.
    /// </summary>
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
