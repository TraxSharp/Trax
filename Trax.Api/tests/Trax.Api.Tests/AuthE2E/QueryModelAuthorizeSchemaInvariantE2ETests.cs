using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Defense-in-depth coverage for <c>QueryModelAuthorizationSchemaValidator</c>.
/// The validator runs at host start, materialises the GraphQL schema, and
/// reasserts that every <c>[TraxAuthorize]</c>-gated entity still carries
/// its <c>@authorize</c> directive at both type level and entry-field level.
///
/// <para>
/// The point of these tests is to prove that the gate survives the full
/// configuration pipeline end-to-end. A separate suite of unit tests
/// (<c>QueryModelAuthorizationSchemaValidatorTests</c>) exercises the
/// validator directly against hand-rolled schemas where the directive has
/// been stripped, since the "naive" bypass attempts via <c>ConfigureSchema</c>
/// (e.g. duplicate <c>ObjectType</c> registrations) get rejected by
/// HotChocolate's own type-uniqueness check before the validator even runs.
/// That's a fine outcome — the host still fails to start, the security
/// posture holds — but it does not exercise the validator's code path.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class QueryModelAuthorizeSchemaInvariantE2ETests
{
    private const string Database = "trax_api_auth_schema_inv";

    [OneTimeSetUp]
    public void SeedDatabase() =>
        AuthzTestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));

    /// <summary>
    /// Baseline: a normally-configured host starts cleanly and the validator
    /// signs off on the materialised schema. Pins that the validator is not
    /// over-zealous and rejecting valid schemas.
    /// </summary>
    [Test]
    public async Task NormalHost_PassesValidator_AndHostStartsSuccessfully()
    {
        using var host = await BuildHostAsync();
        host.Should().NotBeNull();
        // Reaching here means StartAsync completed without the validator
        // throwing — every gated entity passed both type-level and entry-field
        // directive checks against the real, fully-built schema.
    }

    private static async Task<IHost> BuildHostAsync()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthorization(opts =>
                            opts.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );

                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJson()
                                )
                                .AddMediator(
                                    typeof(QueryModelAuthorizeSchemaInvariantE2ETests).Assembly
                                )
                        );

                        services.AddDbContextFactory<AuthzTestDbContext>(o =>
                            o.UseNpgsql(connectionString)
                        );

                        services.AddTraxGraphQL(graphql =>
                            graphql.AddDbContext<AuthzTestDbContext>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                            endpoints.MapGraphQL("/trax/graphql", "trax")
                        );
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }
}
