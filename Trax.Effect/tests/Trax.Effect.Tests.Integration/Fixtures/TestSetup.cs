using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;

namespace Trax.Effect.Tests.Integration.Fixtures;

[TestFixture]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; } = null!;

    public AsyncServiceScope Scope { get; private set; }

    public IDataContextProviderFactory DataContextFactory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        var connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );

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
                        .AddJunctionLogger(serializeJunctionData: true)
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
        Scope = ServiceProvider.CreateAsyncScope();
        DataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        var cleanupContext = (IDataContext)DataContextFactory.Create();
        await using var cleanupContextDisposal = cleanupContext.ConfigureAwait(false);
        await CleanupDatabase(cleanupContext);
    }

    private static async Task CleanupDatabase(IDataContext dataContext)
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

    [TearDown]
    public async Task TestTearDown()
    {
        await Scope.DisposeAsync().ConfigureAwait(false);
    }
}
