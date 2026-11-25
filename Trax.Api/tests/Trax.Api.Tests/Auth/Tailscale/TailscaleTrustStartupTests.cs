using AwesomeAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Tailscale;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Api.Tests.Auth.Tailscale;

/// <summary>
/// A host that registers the tailnet sign-in scheme starts only where its deployment says it is reached only through a
/// Tailscale proxy of its own, and with the proxy's secret configured. Development is no exception.
///
/// <para>Guard for <c>Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md"
)]
public class TailscaleTrustStartupTests
{
    private const string Secret = "startup-proxy-secret-0123456789abcdefghij";

    [Test]
    public async Task A_host_exposed_on_a_tailnet_with_the_secret_starts()
    {
        var act = () => StartAsync(DeployExposure.Tailnet, Secret);

        await act.Should().NotThrowAsync();
    }

    [TestCase(null)]
    [TestCase("public")]
    [TestCase("Tailnet")]
    [TestCase("")]
    public async Task A_host_not_exposed_only_on_a_tailnet_refuses_to_start(string? exposure)
    {
        var act = () => StartAsync(exposure, Secret);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{DeployExposure.ConfigurationKey}*'{DeployExposure.Tailnet}'*");
    }

    [Test]
    public async Task Development_does_not_lift_the_refusal()
    {
        var act = () => StartAsync("public", Secret, Environments.Development);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_host_with_no_proxy_secret_refuses_to_start()
    {
        var act = () => StartAsync(DeployExposure.Tailnet, secret: null);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*proxy's secret*none is configured*");
    }

    [Test]
    public async Task A_short_proxy_secret_refuses_to_start_without_repeating_it()
    {
        var act = () => StartAsync(DeployExposure.Tailnet, "too-short-secret");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("shorter")
            .And.NotContain("too-short-secret");
    }

    [Test]
    public async Task A_secret_set_in_code_counts()
    {
        var act = () => StartAsync(DeployExposure.Tailnet, secret: null, configureSecret: Secret);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_default_cors_policy_that_allows_any_origin_refuses_to_start()
    {
        var act = () =>
            StartAsync(
                DeployExposure.Tailnet,
                Secret,
                cors: c => c.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader())
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CORS*any origin*");
    }

    [Test]
    public async Task A_default_cors_policy_whose_predicate_admits_a_foreign_origin_refuses_to_start()
    {
        var act = () =>
            StartAsync(
                DeployExposure.Tailnet,
                Secret,
                cors: c => c.AddDefaultPolicy(p => p.SetIsOriginAllowed(_ => true).AllowAnyHeader())
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CORS*any origin*");
    }

    [Test]
    public async Task A_default_cors_policy_that_names_its_origins_starts()
    {
        var act = () =>
            StartAsync(
                DeployExposure.Tailnet,
                Secret,
                cors: c =>
                    c.AddDefaultPolicy(p =>
                        p.WithOrigins("https://shop.example.com").AllowAnyHeader()
                    )
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void A_scheme_that_admits_nobody_is_refused_at_registration()
    {
        var act = () => new ServiceCollection().AddTraxTailscaleAuth(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*admits nobody*");
    }

    private static async Task StartAsync(
        string? exposure,
        string? secret,
        string environment = "Production",
        string? configureSecret = null,
        Action<CorsOptions>? cors = null
    )
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (exposure is not null)
            settings[DeployExposure.ConfigurationKey] = exposure;
        if (secret is not null)
            settings[DeployExposure.ProxySecretKey] = secret;
        using var host = new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                if (cors is not null)
                    services.AddCors(cors);
                services.AddTraxTailscaleAuth(
                    tailnet => tailnet.Login("admin@example.com", "Admin"),
                    configureSecret is null ? null : o => o.ProxySecret = configureSecret
                );
            })
            .Build();
        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
