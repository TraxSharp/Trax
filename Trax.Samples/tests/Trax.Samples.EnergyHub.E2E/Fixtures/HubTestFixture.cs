using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.EnergyHub.E2E.HubTests;
using Trax.Samples.EnergyHub.E2E.Utilities;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.EnergyHub.E2E.Fixtures;

[TestFixture]
public abstract class HubTestFixture
{
    protected IServiceScope Scope { get; private set; } = null!;

    protected ITrainBus TrainBus { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    [SetUp]
    public virtual async Task SetUp()
    {
        var services = SharedHubSetup.Factory.Services;
        Scope = services.CreateScope();
        TrainBus = Scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var dataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        DataContext = (IDataContext)dataContextFactory.Create();

        await CleanExecutionData();
    }

    [TearDown]
    public void TearDown()
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        Scope.Dispose();
    }

    protected void EnableManifestManager()
    {
        var config = SharedHubSetup.Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = true;
    }

    protected void DisableManifestManager()
    {
        var config = SharedHubSetup.Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
    }

    protected SchedulerConfiguration GetSchedulerConfiguration()
    {
        return SharedHubSetup.Factory.Services.GetRequiredService<SchedulerConfiguration>();
    }

    protected HttpClient GetHttpClient()
    {
        return SharedHubSetup.Factory.CreateClient();
    }

    protected GraphQLClient GetGraphQLClient()
    {
        return new GraphQLClient(GetHttpClient());
    }

    private async Task CleanExecutionData()
    {
        await DataContext.BackgroundJobs.ExecuteDeleteAsync();
        await DataContext.Logs.ExecuteDeleteAsync();
        await DataContext.WorkQueues.ExecuteDeleteAsync();
        await DataContext.DeadLetters.ExecuteDeleteAsync();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();
    }
}
