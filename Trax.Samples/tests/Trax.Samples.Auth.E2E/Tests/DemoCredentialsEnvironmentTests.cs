using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.E2E.Factories;
using Trax.Samples.Auth.E2E.Utilities;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// The demo API keys and the demo JWT signing key are published in this repository, so they exist
/// only in Development. Started in Production the host registers neither: the published keys and
/// tokens signed with the published key authenticate nobody, and only credentials from
/// configuration (hashed API keys, an identity provider) do.
///
/// <para>Enforces <c>Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md")]
public class DemoCredentialsEnvironmentTests
{
    private const string Adr =
        "see Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md";

    private const string WhoAmI = "{ discover { whoAmI { id roles } } }";

    private const string Health = "{ operations { health { status } } }";

    private const string BotKey = "a-real-key-that-only-configuration-holds";

    private TestJwksServer _identityProvider = null!;

    [OneTimeSetUp]
    public async Task StartIdentityProvider() =>
        _identityProvider = await TestJwksServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopIdentityProvider() => await _identityProvider.DisposeAsync();

    [Test]
    public async Task Development_RegistersTheDemoSchemes()
    {
        var schemes = await SchemeNames(SharedAuthSetup.Factory);

        schemes
            .Should()
            .Contain(
                [ApiKeyDefaults.SchemeName, JwtDefaults.SchemeName],
                "the premise of the Production tests"
            );
    }

    [Test]
    public async Task Production_WithNothingConfigured_RegistersNoScheme_AndRefusesTheDemoCredentials()
    {
        await using var factory = new ProductionAuthApiFactory();
        var graphQL = new GraphQLClient(factory.CreateClient());

        (await SchemeNames(factory)).Should().BeEmpty(Adr);

        (await graphQL.SendAsync(Health, DemoOscarKey)).IsRefused.Should().BeTrue(Adr);
        (await graphQL.SendAsync(WhoAmI, DemoAliceToken)).IsRefused.Should().BeTrue(Adr);

        var echo = await graphQL.SendAsync(
            """{ discover { echo(input: { message: "still public" }) { echoed } } }""",
            Caller.Anonymous
        );
        echo.HasErrors.Should().BeFalse("the [TraxAllowAnonymous] train needs no scheme");
    }

    [Test]
    public async Task Production_DoesNotServeTheTokenEndpoint()
    {
        await using var factory = new ProductionAuthApiFactory();

        var response = await factory.CreateClient().GetAsync("/dev/token/alice");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, Adr);
    }

    [Test]
    public async Task Production_RefusesToStart_WithADemoKeyRegistered()
    {
        // What happens when someone copies the Development block out of its if: Trax.Api refuses
        // to start the host, naming the marker and the environment.
        await using var factory = new ProductionAuthApiFactory(configure: builder =>
            builder.ConfigureTestServices(services =>
                services.AddTraxApiKeyAuth(keys =>
                    keys.Add(DemoCredentials.AliceKey, DemoCredentials.Alice.ToApiKeyPrincipal)
                )
            )
        );

        var start = () => factory.Services;

        start
            .Should()
            .Throw<Exception>()
            .Where(e => e.ToString().Contains("do-not-use-in-production"), Adr);
    }

    [Test]
    public async Task Production_AcceptsConfiguredCredentials_AndStillRefusesTheDemoOnes()
    {
        await using var factory = ConfiguredProductionHost();
        var graphQL = new GraphQLClient(factory.CreateClient());

        // A hashed key from configuration authenticates, with the roles configuration gave it.
        var bot = Caller.WithApiKey("ops bot", BotKey);
        var whoAmI = await graphQL.SendAsync(WhoAmI, bot);
        whoAmI.HasErrors.Should().BeFalse(whoAmI.Raw);
        whoAmI.GetData("discover", "whoAmI", "id").GetString().Should().Be("TraxApiKey:ops-bot");
        (await graphQL.SendAsync(Health, bot)).HasErrors.Should().BeFalse();

        // A token from the identity provider authenticates through the JWT scheme.
        var idpToken = _identityProvider
            .CreateIssuer("trax-samples-auth")
            .Mint(b => b.WithSubject("u-123").WithRole(AuthRoles.Operator));
        var viaIdp = await graphQL.SendAsync(WhoAmI, Caller.WithToken("idp user", idpToken));
        viaIdp.HasErrors.Should().BeFalse(viaIdp.Raw);
        viaIdp.GetData("discover", "whoAmI", "id").GetString().Should().Be("TraxJwt:u-123");

        // Both schemes exist here, and still neither accepts a demo credential.
        (await graphQL.SendAsync(Health, DemoOscarKey))
            .IsRefused.Should()
            .BeTrue(Adr);
        (await graphQL.SendAsync(WhoAmI, DemoAliceToken)).IsRefused.Should().BeTrue(Adr);
    }

    private static Caller DemoOscarKey => Caller.WithApiKey("demo oscar", DemoCredentials.OscarKey);

    private static Caller DemoAliceToken =>
        Caller.WithToken("demo alice", DemoCredentials.MintToken(DemoCredentials.Alice));

    private ProductionAuthApiFactory ConfiguredProductionHost()
    {
        // What a secret store would hold for the key: a salt and SHA-256(salt || key).
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(BotKey)]);

        return new ProductionAuthApiFactory(
            new Dictionary<string, string>
            {
                ["Auth:ApiKeys:0:Id"] = "ops-bot",
                ["Auth:ApiKeys:0:Salt"] = Convert.ToBase64String(salt),
                ["Auth:ApiKeys:0:Hash"] = Convert.ToBase64String(hash),
                ["Auth:ApiKeys:0:Roles:0"] = AuthRoles.Operator,
                ["Auth:Jwt:Authority"] = _identityProvider.Issuer,
                ["Auth:Jwt:Audience"] = "trax-samples-auth",
            },
            builder =>
                builder.ConfigureTestServices(services =>
                    // The test identity provider serves its metadata over plain HTTP. A real one
                    // is HTTPS, which the scheme requires by default.
                    services.Configure<JwtBearerOptions>(
                        JwtDefaults.SchemeName,
                        options => options.RequireHttpsMetadata = false
                    )
                )
        );
    }

    private static async Task<List<string>> SchemeNames(AuthApiFactory factory)
    {
        var schemes = await factory
            .Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        return schemes.Select(s => s.Name).ToList();
    }
}
