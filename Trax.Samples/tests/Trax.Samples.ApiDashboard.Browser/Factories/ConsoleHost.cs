using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Samples.ApiDashboard.Browser.Factories;

/// <summary>
/// The host the console's browser tests open, in the test process on Kestrel, on a port of its own: Trax on
/// Postgres (<c>api_dashboard_browser_tests</c>, the one database CI provisions for this suite, Samples ADR 0001),
/// the scheduler with fast polling, the state machines of <c>Host/BrowserHostModel.cs</c>, the GraphQL endpoint
/// behind API keys with the operations gate and persisted operations required, and the dashboard. The console
/// registers its own documents at startup (Api ADR 0042), so it sends ids, never text.
/// </summary>
public sealed class ConsoleHost : IAsyncDisposable
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=api_dashboard_browser_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    /// <summary>The role the operations gate admits.</summary>
    public const string OperatorRole = "Operator";

    /// <summary>The role persisted-operation management admits.</summary>
    public const string ApiAdminRole = "ApiAdmin";

    /// <summary>A log category the host configures besides <c>Default</c>, which a test may change.</summary>
    public const string LoggedCategory = "Trax.Samples.ApiDashboard.Browser";

    public static string ConnectionString { get; } =
        new NpgsqlConnectionStringBuilder(DefaultConnectionString)
        {
            Port = int.TryParse(
                Environment.GetEnvironmentVariable("TRAX_TEST_PG_PORT"),
                out var port
            )
                ? port
                : 5432,
        }.ConnectionString;

    private readonly WebApplication _app;

    private ConsoleHost(WebApplication app, Uri address)
    {
        _app = app;
        Address = address;
    }

    /// <summary>The origin the console is served from, with a trailing slash.</summary>
    public Uri Address { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Whether the endpoint refuses document text (it always does here; the checks read this).</summary>
    public bool RequiresPersistedOperations => true;

    /// <summary>The console's own address for <paramref name="route"/>, under its base path.</summary>
    public Uri Console(string route = "") =>
        new(Address, "/trax/dashboard/" + route.TrimStart('/'));

    /// <summary>Starts the host on an empty <c>trax</c> schema, so every run sees a first start.</summary>
    public static async Task<ConsoleHost> StartAsync()
    {
        await DropTraxSchemaAsync();
        var app = Build();
        await app.StartAsync();
        var address = new Uri(
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single()
        );
        return new ConsoleHost(app, address);
    }

    /// <summary>Runs <paramref name="query"/> on a fresh data context, so it sees what the host committed.</summary>
    public async Task<T> QueryAsync<T>(Func<IDataContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var dataContext = await scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        try
        {
            return await query(dataContext);
        }
        finally
        {
            (dataContext as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The host's machine sweep: it applies invoked runs' outcomes that their own host missed, and fires due timers.
    /// A seed that needs a timer left due or an outcome left unapplied stops it for the test (Seed.cs).
    /// </summary>
    public IHostedService MachineSweep =>
        Services
            .GetServices<IHostedService>()
            .Single(s => s.GetType().Name == "InvokeOutcomeReconciler");

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    private static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // Two configured categories, so the scheduler page has log levels to show and change.
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                [$"Logging:LogLevel:{LoggedCategory}"] = "Warning",
            }
        );

        builder.Services.AddTraxApiKeyAuth(keys =>
        {
            foreach (var caller in Caller.All)
                keys.Add(caller.Key, id: caller.Id, [.. caller.Roles]);
        });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ISnapshotPrincipal, NoSnapshotPrincipal>();
        builder.Services.AddSingleton<Outages>();
        builder.Services.AddSingleton<Holds>();
        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UsePostgres(ConnectionString)
                        .SaveTrainParameters()
                        .AddDecisionRecording()
                        .AddJunctionEvents()
                        .AddJunctionProgress()
                        // A run's steps, so the run page places them on its graph and timeline.
                        .AddJunctionEvents()
                )
                .AddStateMachines(typeof(GateMachine).Assembly)
                .AddMediator(typeof(ConsoleHost).Assembly)
                .AddScheduler(scheduler =>
                    scheduler
                        // A failure is dead-lettered, and a requeue dispatched, within a second or two.
                        .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                        .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1))
                        .DefaultRetryDelay(TimeSpan.FromSeconds(1))
                        .ConfigureLocalWorkers(workers =>
                            workers.PollingInterval = TimeSpan.FromMilliseconds(250)
                        )
                        .Schedule<IRefreshRatesTrain>(
                            "refresh-rates",
                            new RatesInput(),
                            Every.Seconds(2),
                            o => o.Group("ingest")
                        )
                        .ThenInclude<IRepriceCatalogTrain>(
                            "reprice-catalog",
                            new RatesInput(),
                            o => o.Group("publish")
                        )
                )
        );
        builder.Services.AddTraxGraphQL(graphql =>
            graphql
                .ExposeOperationQueries()
                .ExposeOperationMutations()
                .GateOperations(roles: OperatorRole)
                .RequireAuthorization()
                .UsePersistedOperations(po =>
                    po.SingleNode().RequirePersisted(true).GateManagement(roles: ApiAdminRole)
                )
        );

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTraxGraphQL();
        // The GraphQL endpoint authorizes everything the app reads; the shell is open here because
        // a browser cannot present the API key on a page load (Trax.Api/docs/adr/0039).
        app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());
        return app;
    }

    private static async Task DropTraxSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS trax CASCADE;",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
