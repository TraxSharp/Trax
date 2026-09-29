using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Samples.GameServer.Api;
using Trax.Samples.GameServer.Auth;
using Trax.Samples.GameServer.E2E.Factories;

namespace Trax.Samples.GameServer.E2E.ApiTests;

/// <summary>
/// The GameServer API's demo API keys and its two HS256 JWT schemes use keys published in this
/// repository, so they exist only in Development. Started in Production, the host registers
/// none of them, so neither the published admin key nor a token signed with a published HS256
/// key authenticates.
///
/// <para>Enforces <c>Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md")]
public class DemoCredentialsEnvironmentTests
{
    private const string Adr =
        "see Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md";

    private const string ApiKeyScheme = "TraxApiKey";

    private sealed class ProductionFactory : GameServerApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");

            // Disposing a second host in this process, next to the shared API factory, throws
            // AlreadyClosedException from the RabbitMQ receiver's StopAsync. These tests read
            // the auth schemes only, so the receiver is left out.
            builder.ConfigureTestServices(services =>
            {
                foreach (
                    var receiver in services
                        .Where(d =>
                            d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType?.Name == "TrainEventReceiverService"
                        )
                        .ToList()
                )
                    services.Remove(receiver);
            });
        }
    }

    [Test]
    public async Task Production_registers_no_demo_scheme()
    {
        await using var factory = new ProductionFactory();

        var schemes = await SchemeNames(factory);

        schemes
            .Should()
            .NotContain(new[] { ApiKeyScheme, DemoJwt.PlayerScheme, DemoJwt.PartnerScheme }, Adr);
    }

    [Test]
    public async Task Development_registers_the_demo_schemes()
    {
        var schemes = await SchemeNames(SharedApiSetup.Factory);

        schemes
            .Should()
            .Contain(
                new[] { ApiKeyScheme, DemoJwt.PlayerScheme, DemoJwt.PartnerScheme },
                "the premise of the Production test"
            );
    }

    [Test]
    public async Task Production_refuses_the_demo_admin_key()
    {
        await using var factory = new ProductionFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKeyDefaults.AdminKey);

        var response = await client.PostAsJsonAsync(
            "/trax/graphql",
            new { query = "{ operations { trains { serviceTypeName } } }" }
        );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, body);
        body.Should().Contain("TRAX_AUTHORIZATION", Adr).And.NotContain("serviceTypeName\":\"");
    }

    private static async Task<List<string>> SchemeNames(GameServerApiFactory factory)
    {
        var schemes = await factory
            .Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        return schemes.Select(s => s.Name).ToList();
    }
}
