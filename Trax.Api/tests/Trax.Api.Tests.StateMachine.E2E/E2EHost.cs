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
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

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
    public const string AdminApiKey = "sm-e2e-admin-key";

    /// <summary>A user who holds both roles the wizard's rules name.</summary>
    public const string AliceKey = "sm-e2e-alice-key";

    /// <summary>A user with no roles.</summary>
    public const string BobKey = "sm-e2e-bob-key";

    /// <summary>A user who may pay but not approve.</summary>
    public const string CarolKey = "sm-e2e-carol-key";

    /// <summary>An operator (with Admin as well), who reaches the operations surface and no user's draft.</summary>
    public const string OperatorKey = "sm-e2e-operator-key";

    /// <summary>The role the operations surface is gated to when a host exposes it.</summary>
    public const string OperatorRole = "Operator";

    // The always-present `postgres` maintenance database (local docker-compose and CI differ on app dbs).
    private static readonly string Maintenance =
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";

    public static string ConnectionString(string database) => ConnectionString(database, null);

    /// <summary>
    /// The connection string of <paramref name="database"/>, naming its sessions
    /// <paramref name="applicationName"/> when one is given, so a test can find and terminate them in
    /// <c>pg_stat_activity</c>.
    /// </summary>
    public static string ConnectionString(string database, string? applicationName) =>
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={database};"
        + "Include Error Detail=true;Maximum Pool Size=20"
        + (applicationName is null ? "" : $";Application Name={applicationName}");

    /// <summary>
    /// Drop and recreate a throwaway database. The snapshot tables come with the rest of the Trax schema from
    /// <c>UsePostgres</c>'s migrations when the host builds.
    /// </summary>
    public static async Task RecreateDatabaseAsync(string database)
    {
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await Exec(admin, $"CREATE DATABASE {database}");
    }

    public static async Task DropDatabaseAsync(string database)
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
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
                    {
                        services.AddLogging();
                        services.AddRouting();

                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminApiKey, id: "admin", "Admin")
                                .Add(
                                    AliceKey,
                                    id: "alice",
                                    WizardMachine.PayerRole,
                                    WizardMachine.ApproverRole
                                )
                                .Add(BobKey, id: "bob")
                                .Add(CarolKey, id: "carol", WizardMachine.PayerRole)
                                .Add(OperatorKey, id: "operator", OperatorRole, "Admin")
                        );
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

                        // The two things a machine can't know: map auth to a user key, and the effect impl.
                        services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
                        services.AddSingleton(charge);

                        if (options.ExposeOperations)
                            AddOperationsServices(services);

                        services.AddTraxApi();
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
                                (options.Machines is null && options.Served.Count == 0)
                                || services.Any(d =>
                                    d.ServiceType == typeof(IPersistedOperationsCapability)
                                )
                                ? graphql
                                : MachineDocumentOps.Persisted()(graphql);
                        });

                        options.Services?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGraphQL("/trax/graphql", "trax");
                            options.Endpoints?.Invoke(endpoints);
                        });
                    })
            )
            .Build();

        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return host;
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

        var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
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
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoJobSubmitter : IJobSubmitter
    {
        public Task<string> EnqueueAsync(long metadataId) =>
            throw new InvalidOperationException("This host runs no trains.");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new InvalidOperationException("This host runs no trains.");
    }

    /// <summary>A scheduler facade these hosts never call: every member throws.</summary>
    public class NoScheduler : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("This host runs no scheduler.");
    }
}

/// <summary>What an <see cref="E2EHost"/> adds to the default host.</summary>
public sealed record E2EHostOptions
{
    /// <summary>Configures the state machines: the generic opt-in, the TTL, the sweeps.</summary>
    public Action<StateMachineOptions>? Machines { get; init; }

    /// <summary>
    /// The fakes the host serves through their typed operations: each is registered as its subclass marked
    /// <c>[TraxMachine]</c> (<see cref="E2EMachines"/>). Every other machine of the assembly is registered unmarked.
    /// </summary>
    public IReadOnlyList<Type> Served { get; init; } = [];

    /// <summary>Applied to the GraphQL builder after the operations surface, when that is exposed.</summary>
    public Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? GraphQL { get; init; }

    /// <summary>
    /// Exposes the operations queries and mutations, gated to <see cref="E2EHost.OperatorRole"/>, over the
    /// shared operations service. With it on, every train's lifecycle is published to the operator.
    /// </summary>
    public bool ExposeOperations { get; init; }

    /// <summary>Registrations made after Trax's, so the last one wins.</summary>
    public Action<IServiceCollection>? Services { get; init; }

    /// <summary>Endpoints mapped beside the GraphQL endpoint, such as the operations console.</summary>
    public Action<IEndpointRouteBuilder>? Endpoints { get; init; }

    /// <summary>Stores every run's input and output on its row (<c>SaveTrainParameters</c>), as most hosts do.</summary>
    public bool SaveTrainParameters { get; init; }

    /// <summary>The <c>application_name</c> of the host's database sessions.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>
    /// Authentication schemes registered beside the API keys, before the GraphQL endpoint, as a host registers its
    /// schemes: a JWT scheme, for the tests about a socket a token authenticated.
    /// </summary>
    public Action<IServiceCollection>? Authentication { get; init; }

    /// <summary>A host that serves <see cref="WizardMachine"/> through its typed operations.</summary>
    public static E2EHostOptions Wizard() => new() { Served = [typeof(WizardMachine)] };

    /// <summary>The policy <see cref="GenericOptIn"/> gates the generic mutations with: any signed-in caller.</summary>
    public const string GenericPolicy = "sm-e2e-generic";

    /// <summary>Opts in to the generic <c>stateMachine</c> mutations behind <see cref="GenericPolicy"/>.</summary>
    public static Action<StateMachineOptions> OptInToGeneric { get; } =
        o => o.ExposeGenericStateMachineMutations(g => g.Policy = GenericPolicy);

    /// <summary>Registers <see cref="GenericPolicy"/>, which any authenticated caller satisfies.</summary>
    public static void AddGenericPolicy(IServiceCollection services) =>
        services.AddAuthorization(o =>
            o.AddPolicy(GenericPolicy, p => p.RequireAuthenticatedUser())
        );

    /// <summary>A host that serves the generic mutations to any signed-in caller.</summary>
    public static E2EHostOptions GenericOptIn() =>
        new() { Machines = OptInToGeneric, Services = AddGenericPolicy };
}
