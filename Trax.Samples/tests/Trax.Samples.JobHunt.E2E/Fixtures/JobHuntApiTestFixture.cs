using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.JobHunt.Auth;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.E2E.Utilities;

namespace Trax.Samples.JobHunt.E2E.Fixtures;

[TestFixture]
public abstract class JobHuntApiTestFixture
{
    protected HttpClient HttpClient { get; private set; } = null!;

    protected GraphQLClient GraphQL { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    protected JobHuntDbContext JobHuntDb { get; private set; } = null!;

    private IServiceScope Scope { get; set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        HttpClient = SharedJobHuntHubSetup.Factory.CreateClient();
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
        Scope = SharedJobHuntHubSetup.Factory.Services.CreateScope();

        var dataContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        DataContext = (IDataContext)dataContextFactory.Create();

        JobHuntDb = Scope.ServiceProvider.GetRequiredService<JobHuntDbContext>();

        await CleanData();
    }

    [TearDown]
    public void TearDown()
    {
        JobHuntDb.Dispose();

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        Scope.Dispose();
    }

    protected static string AliceKey => ApiKeyDefaults.AliceKey;
    protected static string BobKey => ApiKeyDefaults.BobKey;
    protected static string CharlieKey => ApiKeyDefaults.CharlieKey;

    private async Task CleanData()
    {
        // Clean JobHunt domain data (in FK dependency order).
        await JobHuntDb.EmailsSent.ExecuteDeleteAsync();
        await JobHuntDb.EmailDrafts.ExecuteDeleteAsync();
        await JobHuntDb.Contacts.ExecuteDeleteAsync();
        await JobHuntDb.Applications.ExecuteDeleteAsync();
        await JobHuntDb.JobSnapshots.ExecuteDeleteAsync();
        await JobHuntDb.Artifacts.ExecuteDeleteAsync();
        await JobHuntDb.WatchedCompanies.ExecuteDeleteAsync();
        await JobHuntDb.Jobs.ExecuteDeleteAsync();
        await JobHuntDb.Profiles.ExecuteDeleteAsync();
        await JobHuntDb.Users.ExecuteDeleteAsync();

        // Clean Trax execution data.
        await DataContext.Logs.ExecuteDeleteAsync();
        await DataContext.WorkQueues.ExecuteDeleteAsync();
        await DataContext.DeadLetters.ExecuteDeleteAsync();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();
    }
}
