using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.ContentShield.E2E.ApiTests;
using Trax.Samples.ContentShield.E2E.Utilities;

namespace Trax.Samples.ContentShield.E2E.Fixtures;

[TestFixture]
public abstract class ApiTestFixture
{
    /// <summary>The moderator demo key the API registers in Development.</summary>
    protected const string ModeratorKey = Api.DemoKeys.ModeratorKey;

    protected AsyncServiceScope Scope { get; private set; }

    protected IDataContext DataContext
    {
        get => field.NotNull(nameof(DataContext));
        private set;
    }

    internal static TestRunner Runner => SharedApiSetup.Runner;

    [SetUp]
    public virtual async Task SetUp()
    {
        Scope = SharedApiSetup.Factory.Services.CreateAsyncScope();
        DataContext = (IDataContext)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();

        await DataContext.WorkQueues.ExecuteDeleteAsync();
        await DataContext.Logs.ExecuteDeleteAsync();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        await Scope.DisposeAsync().ConfigureAwait(false);
    }

    protected static HttpClient GetHttpClient() => SharedApiSetup.Factory.CreateClient();

    protected static GraphQLClient GetGraphQLClient() => new(GetHttpClient());
}
