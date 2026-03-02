using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Trax.Effect.Tests.Data.Postgres.Integration;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; }

    public IServiceScope Scope { get; private set; }

    public ITrainBus TrainBus { get; private set; }

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
            .AddTraxEffects(options =>
                options
                    .AddServiceTrainBus(
                        assemblies:
                        [
                            typeof(AssemblyMarker).Assembly,
                            typeof(Trax.Effect.Tests.Integration.AssemblyMarker).Assembly,
                        ]
                    )
                    .SetEffectLogLevel(LogLevel.Information)
                    .SaveTrainParameters()
                    .AddPostgresEffect(connectionString)
                    .AddEffectDataContextLogging(minimumLogLevel: LogLevel.Trace)
                    .AddJsonEffect()
                    .AddStepLogger(serializeStepData: true)
            )
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

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var cleanupContext = (IDataContext)factory.Create();
        await CleanupDatabase(cleanupContext);
    }

    /// <summary>
    /// Deletes all rows from all scheduler tables in FK-safe order to ensure
    /// complete test isolation between runs.
    /// </summary>
    private static async Task CleanupDatabase(IDataContext dataContext)
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

    [TearDown]
    public async Task TestTearDown()
    {
        Scope.Dispose();
    }
}
