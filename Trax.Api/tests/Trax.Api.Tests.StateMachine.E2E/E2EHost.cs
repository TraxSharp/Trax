using System.Net.Http.Json;
using System.Text.Json;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Mutations;
using Trax.Mediator.Extensions;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Builds a live in-process Trax GraphQL server (ASP.NET Core via <see cref="TestServer"/>) over a real
/// throwaway Postgres, with the full stack wired the way a real host would: <c>AddTrax</c> +
/// <c>AddTraxStateMachines</c> + <c>AddTraxGraphQL</c> + API-key auth, and <see cref="ISnapshotPrincipal"/>
/// bound over Trax's own <see cref="Trax.Api.Auth.TraxCaller"/>. The four generic <c>stateMachine</c>
/// mutations are driven over HTTP against this host.
/// </summary>
public static class E2EHost
{
    public const string AdminApiKey = "sm-e2e-admin-key";

    // The always-present `postgres` maintenance database (local docker-compose and CI differ on app dbs).
    private const string Maintenance =
        "Host=localhost;Port=5432;Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";

    public static string ConnectionString(string database) =>
        $"Host=localhost;Port=5432;Username=trax;Password=trax123;Database={database};"
        + "Include Error Detail=true;Maximum Pool Size=20";

    /// <summary>Drop and recreate a throwaway database, then create the snapshot tables on it.</summary>
    public static async Task RecreateDatabaseAsync(string database)
    {
        await using (var admin = new NpgsqlConnection(Maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {database}");
        }

        // Create the snapshot_draft + effect_claim tables on the empty database BEFORE the host boots. The
        // Trax framework tables are created by UsePostgres's DbUp migration when the host builds; DbUp's
        // `create schema if not exists trax` is a no-op against the schema EnsureCreated just made, so the
        // two table sets coexist in the `trax` schema.
        await using var db = new SnapshotDbContext(
            new DbContextOptionsBuilder<SnapshotDbContext>()
                .UseNpgsql(ConnectionString(database))
                .Options
        );
        await db.Database.EnsureCreatedAsync();
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
    public static async Task<IHost> StartAsync(string database, IOrderCharge charge)
    {
        var connectionString = ConnectionString(database);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();

                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminApiKey, id: "admin", "Admin")
                        );
                        services.AddAuthorization();

                        // Trax core + mediator. The mediator scan needs the assembly that holds the four
                        // generic mutation trains (they ship in the persistence package, not this one), so
                        // Trax can route them by input type. StateMachineMutations.Assembly names it.
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJson()
                                )
                                .AddMediator(
                                    typeof(E2EHost).Assembly,
                                    StateMachineMutations.Assembly
                                )
                        );

                        // One line: discover the machines and wire the store, the claim ledger, the
                        // exactly-once runner, and the machine registry.
                        services.AddTraxStateMachines(typeof(E2EHost).Assembly);

                        // The two things a machine can't know: map auth to a user key, and the effect impl.
                        services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
                        services.AddSingleton(charge);

                        services.AddDbContext<SnapshotDbContext>(o =>
                            o.UseNpgsql(connectionString)
                        );

                        services.AddTraxApi();
                        // The four stateMachine trains are all mutations; the Ping query train (this
                        // assembly) keeps the root Query type non-empty so HotChocolate can build.
                        services.AddTraxGraphQL(graphql => graphql);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                            endpoints.MapGraphQL("/trax/graphql", "trax")
                        );
                    })
            )
            .Build();

        await host.StartAsync();
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

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
