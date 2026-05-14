using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxJwtRejectAuthenticationHandlerTests
{
    [Test]
    public async Task AuthenticateAsync_FailsWithKnownMessage()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/auth-result");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("No JWT scheme matched");
    }

    [Test]
    public async Task ChallengeAsync_Returns401()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/challenge");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ForbidAsync_Returns403()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/forbid");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<IHost> BuildHost()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLogging();
                        services
                            .AddAuthentication(JwtDefaults.RejectSchemeName)
                            .AddScheme<
                                Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                                TraxJwtRejectAuthenticationHandler
                            >(JwtDefaults.RejectSchemeName, _ => { });
                        services.AddAuthorization();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet(
                                "/auth-result",
                                async (HttpContext http) =>
                                {
                                    var result = await http.AuthenticateAsync(
                                        JwtDefaults.RejectSchemeName
                                    );
                                    return Results.Ok(
                                        new
                                        {
                                            result.Succeeded,
                                            FailureMessage = result.Failure?.Message,
                                        }
                                    );
                                }
                            );
                            endpoints.MapGet(
                                "/challenge",
                                async (HttpContext http) =>
                                {
                                    await http.ChallengeAsync(JwtDefaults.RejectSchemeName);
                                }
                            );
                            endpoints.MapGet(
                                "/forbid",
                                async (HttpContext http) =>
                                {
                                    await http.ForbidAsync(JwtDefaults.RejectSchemeName);
                                }
                            );
                        });
                    })
            )
            .Build();
        await host.StartAsync();
        return host;
    }
}
