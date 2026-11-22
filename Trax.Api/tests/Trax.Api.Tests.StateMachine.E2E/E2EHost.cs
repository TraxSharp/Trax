using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Core.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Builds a live in-process Trax GraphQL server (ASP.NET Core via <see cref="TestServer"/>) over a real
/// throwaway Postgres, with the full stack wired the way a real host would: <c>AddTrax</c> +
/// <c>AddStateMachines</c> + <c>AddTraxGraphQL</c> + API-key auth, and <see cref="ISnapshotPrincipal"/>
/// bound over Trax's own <see cref="Trax.Api.Auth.TraxCaller"/>. The six generic <c>stateMachine</c>
/// mutations are driven over HTTP against this host, and, where a host exposes it with
/// <see cref="E2EHostOptions.Wizard"/>, the <see cref="WizardMachine"/>'s typed operations.
/// </summary>
public static class E2EHost
{
    public const string AdminApiKey = "sm-e2e-admin-key-0123456789abcdefghijklm";

    /// <summary>A user who holds both roles the wizard's rules name.</summary>
    public const string AliceKey = "sm-e2e-alice-key-0123456789abcdefghijklm";

    /// <summary>A user with no roles.</summary>
    public const string BobKey = "sm-e2e-bob-key-0123456789abcdefghijklmno";

    /// <summary>A user who may pay but not approve.</summary>
    public const string CarolKey = "sm-e2e-carol-key-0123456789abcdefghijklm";

    /// <summary>An operator (with Admin as well), who reaches the operations surface and no user's draft.</summary>
    public const string OperatorKey = "sm-e2e-operator-key-0123456789abcdefghij";

    /// <summary>The role the operations surface is gated to when a host exposes it.</summary>
    public const string OperatorRole = "Operator";

    // The always-present `postgres` maintenance database (local docker-compose and CI differ on app dbs).
    private static readonly string Maintenance = string.Create(
        CultureInfo.InvariantCulture,
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres;Include Error Detail=true"
    );

    public static string ConnectionString(string database) =>
        ConnectionString(database, applicationName: null);

    /// <summary>
    /// The connection string of <paramref name="database"/>, naming its sessions
    /// <paramref name="applicationName"/> when one is given, so a test can find and terminate them in
    /// <c>pg_stat_activity</c>.
    /// </summary>
    public static string ConnectionString(string database, string? applicationName) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={database};"
        )
        + "Include Error Detail=true;Maximum Pool Size=20"
        + (applicationName is null ? "" : $";Application Name={applicationName}");

    /// <summary>
    /// Drop and recreate a throwaway database. The snapshot tables come with the rest of the Trax schema from
    /// <c>UsePostgres</c>'s migrations when the host builds.
    /// </summary>
    public static async Task RecreateDatabaseAsync(string database)
    {
        var admin = new NpgsqlConnection(Maintenance);
        await using (admin.ConfigureAwait(false))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)")
                .ConfigureAwait(false);
            await Exec(admin, $"CREATE DATABASE {database}").ConfigureAwait(false);
        }
    }

    public static async Task DropDatabaseAsync(string database)
    {
        NpgsqlConnection.ClearAllPools();
        var admin = new NpgsqlConnection(Maintenance);
        await using (admin.ConfigureAwait(false))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)")
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds and starts the host. The passed <paramref name="charge"/> is registered as the singleton
    /// <see cref="IOrderCharge"/> so a test can read its delivery count and prove exactly-once.
    /// </summary>
    public static Task<IHost> StartAsync(string database, IOrderCharge charge) =>
        StartAsync(database, charge, new E2EHostOptions());

    /// <summary>
    /// Builds and starts the host with what <paramref name="options"/> adds: the machines it exposes, the
    /// GraphQL configuration, the operations surface, and registrations made after Trax's.
    /// </summary>
    public static async Task<IHost> StartAsync(
        string database,
        IOrderCharge charge,
        E2EHostOptions options
    )
    {
        var connectionString = ConnectionString(database, options.ApplicationName);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                        ConfigureServices(services, charge, options, connectionString)
                    )
                    .Configure(app => ConfigureApp(app, options))
            )
            .Build();

        try
        {
            await host.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return host;
    }

    private static void ConfigureServices(
        IServiceCollection services,
        IOrderCharge charge,
        E2EHostOptions options,
        string connectionString
    )
    {
        services.AddLogging();
        services.AddRouting();

        AddApiKeys(services);
        options.Authentication?.Invoke(services);
        services.AddAuthorization();

        // Trax core + state machines + mediator in one chain. AddStateMachines wires the store /
        // claim ledger / exactly-once runner / registry over the machines registered below, and
        // contributes the six generic mutation trains to the mediator scan (they ship in the persistence
        // package, not this one) so Trax routes them by input type. The host names neither the
        // snapshot tables nor the mutations' assembly. AddStateMachines precedes AddMediator.
        // The wizard invokes a train, so the host checks a run's cancel flag between junctions.
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                {
                    var configured = effects
                        .UsePostgres(connectionString)
                        .AddJson()
                        .AddJunctionProgress();
                    return options.SaveTrainParameters
                        ? configured.SaveTrainParameters()
                        : configured;
                })
                .AddStateMachines(options.Machines)
                .AddMediator(typeof(E2EHost).Assembly)
        );
        // The machines, registered by hand: the ones the test serves as their marked subclasses.
        E2EMachines.Register(services, options.Served);
        foreach (var machine in options.Instances)
            services.AddSingleton(machine);
        if (options.Views is { } views)
            services.AddSingleton(views);

        // The two things a machine can't know: map auth to a user key, and the effect impl.
        services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
        services.AddSingleton(charge);

        if (options.ExposeOperations)
            AddOperationsServices(services);

        if (options.Trains is { } keep)
            services.AddSingleton<ITrainDiscoveryService>(
                new KeptTrains(new TrainDiscoveryService(services), keep)
            );

        services.AddTraxApi();
        AddHostGraphQL(services, options);

        options.Services?.Invoke(services);
    }

    private static void AddApiKeys(IServiceCollection services) =>
        services.AddTraxApiKeyAuth(keys =>
            keys.Add(AdminApiKey, id: "admin", "Admin")
                .Add(AliceKey, id: "alice", WizardMachine.PayerRole, WizardMachine.ApproverRole)
                .Add(BobKey, id: "bob")
                .Add(CarolKey, id: "carol", WizardMachine.PayerRole)
                .Add(OperatorKey, id: "operator", OperatorRole, "Admin")
        );

    private static void AddHostGraphQL(IServiceCollection services, E2EHostOptions options) =>
        // The six stateMachine trains are all mutations; the Ping query train (this
        // assembly) keeps the root Query type non-empty so HotChocolate can build.
        services.AddTraxGraphQL(graphql =>
        {
            if (options.ExposeOperations)
                graphql = graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .GateOperations(roles: OperatorRole);
            graphql = options.GraphQL?.Invoke(graphql) ?? graphql;
            // A host that exposes machines needs persisted operations; a test that configures them
            // itself keeps its own configuration.
            return
                (
                    options.Machines is null
                    && options.Served.Count == 0
                    && options.Instances.Count == 0
                ) || services.Any(d => d.ServiceType == typeof(IPersistedOperationsCapability))
                ? graphql
                : MachineDocumentOps.Persisted()(graphql);
        });

    private static void ConfigureApp(IApplicationBuilder app, E2EHostOptions options)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGraphQL("/trax/graphql", "trax");
            options.Endpoints?.Invoke(endpoints);
        });
    }

    /// <summary>
    /// Writes a draft straight to the store under <paramref name="ownerKey"/>, as host code does. The generic save
    /// serves only machines with no server-side behaviour, so a test that needs a draft of a guarded machine at a
    /// given state seeds it this way. An API key's owner key is <c>TraxApiKey:{id}</c>.
    /// </summary>
    public static async Task SeedDraftAsync(
        this IHost host,
        string ownerKey,
        Guid id,
        string snapshotJson
    )
    {
        var wire = System.Text.Json.Nodes.JsonNode.Parse(snapshotJson).NotNull().AsObject();
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var stored = await scope
            .ServiceProvider.GetRequiredService<ISnapshotStore>()
            .Upsert(
                ownerKey,
                id,
                new Trax.StateMachine.Snapshot
                {
                    Machine = wire["machine"].NotNull().GetValue<string>(),
                    Version = wire["version"].NotNull().GetValue<int>(),
                    State = wire["state"].NotNull().GetValue<string>(),
                    Context = wire["context"].NotNull().AsObject().DeepClone().AsObject(),
                }
            )
            .ConfigureAwait(false);
        if (!stored)
            throw new InvalidOperationException($"The draft {id} could not be seeded.");
    }

    /// <summary>
    /// POST a GraphQL operation (optionally with variables and an API key). Returns the parsed response
    /// (data + errors, if any). Passing the snapshot as a variable keeps the raw JSON off the query string.
    /// </summary>
    public static async Task<JsonDocument> PostAsync(
        this IHost host,
        string query,
        object? variables = null,
        string? apiKey = null
    )
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = variables is null
                ? JsonContent.Create(new { query })
                : JsonContent.Create(new { query, variables }),
        };
        if (apiKey is not null)
            req.Headers.Add("X-Api-Key", apiKey);

        var res = await client.SendAsync(req).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    // The operations service the dashboard and the API share, without the scheduler's workers: these hosts
    // run no scheduler, and no test calls the operations that need one.
    private static void AddOperationsServices(IServiceCollection services)
    {
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            sp.GetRequiredService<ITrainDiscoveryService>(),
            sp.GetRequiredService<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            sp.GetRequiredService<ITrainExecutionService>(),
            sp
        ));
        services.AddScoped<IJobSubmitter, NoJobSubmitter>();
        services.AddScoped(_ => DispatchProxy.Create<ITraxScheduler, NoScheduler>());
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private sealed class NoJobSubmitter : IJobSubmitter
    {
        public Task<string> EnqueueAsync(long metadataId) =>
            throw new InvalidOperationException("This host runs no trains.");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new InvalidOperationException("This host runs no trains.");
    }

    /// <summary>The trains of <paramref name="all"/> that <paramref name="keep"/> keeps.</summary>
    private sealed class KeptTrains(ITrainDiscoveryService all, Func<TrainRegistration, bool> keep)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() =>
            all.DiscoverTrains().Where(keep).ToList();
    }

    /// <summary>A scheduler facade these hosts never call: every member throws.</summary>
    public class NoScheduler : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("This host runs no scheduler.");
    }
}
