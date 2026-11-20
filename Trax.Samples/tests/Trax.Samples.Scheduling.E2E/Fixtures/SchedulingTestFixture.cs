using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Samples.Scheduling.E2E.Utilities;
using Trax.Samples.Scheduling.Host;
using Trax.Samples.Scheduling.Services;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.Scheduling.E2E.Fixtures;

/// <summary>
/// Base for tests against the shared host. Nothing is cleaned between tests: the scheduler keeps
/// running, so each test marks the newest metadata id before it acts and reads only what came
/// after.
/// </summary>
public abstract class SchedulingTestFixture
{
    protected static Db Db => SharedSchedulingSetup.Db;

    protected static IServiceProvider Services => SharedSchedulingSetup.Factory.Services;

    protected static ExchangeRateFeed RateFeed => Services.GetRequiredService<ExchangeRateFeed>();

    protected static SupplierFeed SupplierFeed => Services.GetRequiredService<SupplierFeed>();

    private IServiceScope? _scope;
    private HttpClient? _http;

    protected ITraxScheduler Scheduler
    {
        get => field.NotNull(nameof(Scheduler));
        private set;
    }

    /// <summary>The GraphQL endpoint, sending the demo operator key.</summary>
    protected GraphQLClient Operator
    {
        get => field.NotNull(nameof(Operator));
        private set;
    }

    /// <summary>The GraphQL endpoint, sending no key.</summary>
    protected GraphQLClient Anonymous
    {
        get => field.NotNull(nameof(Anonymous));
        private set;
    }

    [SetUp]
    public void SetUpScope()
    {
        _scope = Services.CreateScope();
        Scheduler = _scope.NotNull().ServiceProvider.GetRequiredService<ITraxScheduler>();
        _http = SharedSchedulingSetup.Factory.CreateClient();
        Operator = new GraphQLClient(_http.NotNull(), DemoKeys.OperatorKey);
        Anonymous = new GraphQLClient(_http.NotNull(), apiKey: null);
    }

    [TearDown]
    public void TearDownScope()
    {
        _http?.Dispose();
        _scope?.Dispose();
    }
}
