using System.Net;
using System.Text;
using FluentAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A host with no authentication at all, whose type-extension fields are public. The exposure
/// census makes such a field say so with <c>[TraxAllowAnonymous]</c>, and saying so must not
/// make the endpoint depend on authentication services the host never registered.
/// </summary>
[TestFixture]
public class PublicTypeExtensionHostTests
{
    [Test]
    public async Task AHostWithoutAuthentication_ServesItsAllowAnonymousTypeExtensionOverHttp()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());

                        // No AddAuthentication(): nothing on this host is gated.
                        s.AddTraxGraphQL(g =>
                            g.ExposeOperationQueries()
                                .AllowAnonymousOperations()
                                .AddTypeExtension<PublicGreeting>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

        var response = await host.GetTestClient()
            .PostAsync(
                "/trax/graphql",
                new StringContent("""{"query":"{ greeting }"}""", Encoding.UTF8, "application/json")
            );
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("\"greeting\":\"hello\"");
    }

    [ExtendObjectType("RootQuery")]
    public class PublicGreeting
    {
        [TraxAllowAnonymous]
        public string Greeting() => "hello";
    }
}
