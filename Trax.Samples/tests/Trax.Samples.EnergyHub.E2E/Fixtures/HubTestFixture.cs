using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
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

    protected AsyncServiceScope Scope { get; private set; }

    protected AsyncServiceScope WorkerScope { get; private set; }

    protected ITrainBus TrainBus
    {
        get => field.NotNull(nameof(TrainBus));
        private set;
    }

    protected IDataContext DataContext
    {
        get => field.NotNull(nameof(DataContext));
        private set;
    }

    [SetUp]
    public virtual async Task SetUp()
    {
        Scope = SharedHubSetup.Factory.Services.CreateAsyncScope();

        // Trains run on the worker, so a test that runs one directly does it there.
        WorkerScope = SharedHubSetup.Worker.Services.CreateAsyncScope();
        TrainBus = WorkerScope.ServiceProvider.GetRequiredService<ITrainBus>();

        var dataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        DataContext = (IDataContext)dataContextFactory.Create();

        await CleanExecutionData();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        await WorkerScope.DisposeAsync().ConfigureAwait(false);
        await Scope.DisposeAsync().ConfigureAwait(false);
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
        await DataContext.BackgroundJobs.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.Logs.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.WorkQueues.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.DeadLetters.ExecuteDeleteAsync().ConfigureAwait(false);
        await DataContext.Metadatas.ExecuteDeleteAsync().ConfigureAwait(false);
        DataContext.Reset();
    }
}
