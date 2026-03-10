using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; } = null!;

    public IServiceScope Scope { get; private set; } = null!;

    public ITrainBus TrainBus { get; private set; } = null!;

    public IJobRunnerTrain JobRunner { get; private set; } = null!;

    public IDataContext DataContext { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

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
                            .AddStepLogger(serializeStepData: true)
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
        Scope = ServiceProvider.CreateScope();
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
        await dataContext.BackgroundJobs.ExecuteDeleteAsync();
        await dataContext.Logs.ExecuteDeleteAsync();
        await dataContext.WorkQueues.ExecuteDeleteAsync();
        await dataContext.DeadLetters.ExecuteDeleteAsync();
        await dataContext.Metadatas.ExecuteDeleteAsync();

        // Clear self-referencing FK before deleting manifests
        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null));
        await dataContext.Manifests.ExecuteDeleteAsync();

        // Delete manifest groups after manifests (FK dependency)
        await dataContext.ManifestGroups.ExecuteDeleteAsync();

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
