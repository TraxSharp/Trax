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
using Trax.Effect.StepProvider.Logging.Extensions;

namespace Trax.Effect.Tests.Integration.Fixtures;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; } = null!;

    public IServiceScope Scope { get; private set; } = null!;

    public IDataContextProviderFactory DataContextFactory { get; private set; } = null!;

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

        ServiceProvider = new ServiceCollection()
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
        DataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var cleanupContext = (IDataContext)DataContextFactory.Create();
        await CleanupDatabase(cleanupContext);
    }

    private static async Task CleanupDatabase(IDataContext dataContext)
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

    [TearDown]
    public async Task TestTearDown()
    {
        Scope.Dispose();
    }
}
