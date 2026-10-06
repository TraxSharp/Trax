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
    /// <summary>The operator demo key the hub registers in Development.</summary>
    protected const string OperatorKey = Hub.DemoKeys.OperatorKey;

    protected IServiceScope Scope { get; private set; } = null!;

    protected IServiceScope WorkerScope { get; private set; } = null!;

    protected ITrainBus TrainBus { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    [SetUp]
    public virtual async Task SetUp()
    {
        Scope = SharedHubSetup.Factory.Services.CreateScope();

        // Trains run on the worker, so a test that runs one directly does it there.
        WorkerScope = SharedHubSetup.Worker.Services.CreateScope();
        TrainBus = WorkerScope.ServiceProvider.GetRequiredService<ITrainBus>();

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

        WorkerScope.Dispose();
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
