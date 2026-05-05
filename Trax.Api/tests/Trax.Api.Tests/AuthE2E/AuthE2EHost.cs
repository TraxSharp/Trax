using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Shared test-host builder for Trax auth end-to-end tests. Starts a real
/// ASP.NET Core host (via TestServer) with the full Trax stack wired up:
/// <c>AddTrax</c> + <c>AddMediator</c> + <c>AddTraxGraphQL</c> + any
/// combination of auth schemes. Points at the docker-compose Postgres so
/// the mediator's data context is real.
/// </summary>
public static class AuthE2EHost
{
    // Pin pool size and prune idle connections aggressively so the long
    // sequence of fresh hosts in CI can't exhaust Postgres' max_connections.
    // Each AuthE2E test class passes its own database name to keep migrations
    // and advisory locks isolated; CI provisions the per-class DBs upfront.
    public static string ConnectionString(string database) =>
        $"Host=localhost;Port=5432;Database={database};Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    public const string JwtIssuer = "https://trax-e2e-tests";
    public const string JwtAudience = "trax-e2e";
    public static readonly byte[] JwtKey = Encoding.UTF8.GetBytes(new string('e', 32));

    public const string AdminApiKey = "admin-e2e-key";
    public const string PlayerApiKey = "player-e2e-key";

    /// <summary>Flags controlling which auth schemes the host wires up.</summary>
    [Flags]
    public enum Schemes
    {
        None = 0,
        ApiKey = 1,
        Jwt = 2,
    }

    public static async Task<IHost> StartAsync(Schemes schemes, string database)
    {
        var connectionString = ConnectionString(database);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();

                        // Auth — registered conditionally per test.
                        if (schemes.HasFlag(Schemes.ApiKey))
                        {
                            services.AddTraxApiKeyAuth(keys =>
                                keys.Add(AdminApiKey, id: "admin", "Admin", "Player")
                                    .Add(PlayerApiKey, id: "player", "Player")
                            );
                        }
                        if (schemes.HasFlag(Schemes.Jwt))
                        {
                            services.AddTraxJwtAuth(jwt =>
                                jwt.UseSymmetricKey(JwtIssuer, JwtAudience, JwtKey)
                                    .WithClockSkew(TimeSpan.FromSeconds(5))
                            );
                        }

                        // [TraxAuthorize(Policy="AdminPolicy")] requires a
                        // matching ASP.NET Core policy definition.
                        services.AddAuthorization(opts =>
                            opts.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"))
                        );

                        // Trax core + mediator. The mediator scans this
                        // assembly so the test trains are registered.
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJson()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );

                        services.AddTraxApi();

                        services.AddDbContextFactory<TestDbContext>(o =>
                            o.UseNpgsql(connectionString)
                        );

                        services.AddTraxGraphQL(graphql =>
                            graphql
                                // Model-query chain (discover/namespace/entity/nodes/field)
                                // needs depth 6; default is 4. Tests are explicit about
                                // their shape so raising the cap here is safe.
                                .MaxExecutionDepth(8)
                                .AddDbContext<TestDbContext>()
                                .AddTypeExtensions(typeof(AuthE2EHost).Assembly)
                                // Add custom subscription + mutation type
                                // extensions for principal-propagation tests.
                                .ConfigureSchema(b =>
                                    b.AddTypeExtension<TestSubscriptions>()
                                        .AddTypeExtension<TestMutations>()
                                )
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseWebSockets();
                        // AddTraxGraphQL registers its schema under the name
                        // "trax"; MapGraphQL with no schema name targets the
                        // default schema and produces an empty "RootQuery".
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGraphQL("/trax/graphql", "trax");
                            // Parallel endpoint that gates every request on
                            // the combined TraxAuthPolicy, used by tests that
                            // need 401 on bad credentials.
                            endpoints
                                .MapGraphQL("/trax/graphql/protected", "trax")
                                .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Mints an HS256-signed JWT against the E2E test issuer/audience/key.
    /// </summary>
    public static string SignJwt(string sub, string name, params string[] roles)
    {
        var key = new SymmetricSecurityKey(JwtKey);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<System.Security.Claims.Claim> { new("sub", sub), new("name", name) };
        foreach (var role in roles)
            claims.Add(new System.Security.Claims.Claim("role", role));

        var now = DateTime.UtcNow;
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(15),
            signingCredentials: creds
        );
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Executes a GraphQL operation via HTTP POST. Returns the full response
    /// JSON (including errors node if present).
    /// </summary>
    public static Task<System.Text.Json.JsonDocument> PostGraphQLAsync(
        this IHost host,
        string query,
        Action<HttpRequestMessage>? configureRequest = null
    ) => PostAsync(host, "/trax/graphql", query, configureRequest);

    /// <summary>
    /// Same as <see cref="PostGraphQLAsync"/> but hits the endpoint gated on
    /// <c>TraxAuthPolicy</c>. Used to force authentication to run when
    /// multiple schemes are registered and no scheme is the default.
    /// </summary>
    public static Task<System.Text.Json.JsonDocument> PostProtectedGraphQLAsync(
        this IHost host,
        string query,
        Action<HttpRequestMessage>? configureRequest = null
    ) => PostAsync(host, "/trax/graphql/protected", query, configureRequest);

    private static async Task<System.Text.Json.JsonDocument> PostAsync(
        IHost host,
        string path,
        string query,
        Action<HttpRequestMessage>? configureRequest
    )
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { query }),
        };
        configureRequest?.Invoke(req);

        var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        return System.Text.Json.JsonDocument.Parse(body);
    }
}
