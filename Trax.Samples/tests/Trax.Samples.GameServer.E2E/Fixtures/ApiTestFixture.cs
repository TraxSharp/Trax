using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.GameServer.Auth;
using Trax.Samples.GameServer.E2E.ApiTests;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.Fixtures;

[TestFixture]
public abstract class ApiTestFixture
{
    protected HttpClient HttpClient { get; private set; } = null!;

    protected GraphQLClient GraphQL { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    private IServiceScope Scope { get; set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HttpClient = SharedApiSetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(HttpClient);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        HttpClient.Dispose();
    }

    [SetUp]
    public virtual async Task SetUp()
    {
        Scope = SharedApiSetup.Factory.Services.CreateScope();
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

    protected static string AdminKey => ApiKeyDefaults.AdminKey;
    protected static string PlayerKey => ApiKeyDefaults.PlayerKey;

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
