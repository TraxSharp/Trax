using System.Text;
using FluentAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
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
/// A class-level <c>[TraxAuthorize]</c> on a type extension gates every field that extension
/// contributes (api/0003). The census accepts the extension on the strength of that attribute, so
/// every field it contributes has to carry the gate, whether HotChocolate built it from a method or
/// from a property.
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class TypeExtensionPropertyPostureTests
{
    [Test]
    public async Task ClassLevelTraxAuthorize_GatesAFieldBuiltFromAMethod()
    {
        using var host = await StartAsync();

        (await PostAsync(host, "{ secretFromMethod }"))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("method-secret");
    }

    [Test]
    public async Task ClassLevelTraxAuthorize_GatesAFieldBuiltFromAProperty()
    {
        using var host = await StartAsync();

        (await PostAsync(host, "{ secretFromProperty }"))
            .Should()
            .Contain(
                "TRAX_AUTHORIZATION",
                "the census accepted this field because its extension class is [TraxAuthorize]"
            )
            .And.NotContain("property-secret");
    }

    private static async Task<string> PostAsync(IHost host, string query)
    {
        var response = await host.GetTestClient()
            .PostAsync(
                "/trax/graphql",
                new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { query }),
                    Encoding.UTF8,
                    "application/json"
                )
            );
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<IHost> StartAsync() =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxApiKeyAuth(keys => keys.Add("posture-key", id: "reader"));
                        s.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());

                        // An open endpoint: the extension's own posture is the only gate.
                        s.AddTraxGraphQL(g =>
                            g.ExposeOperationQueries()
                                .AllowAnonymousOperations()
                                .AddTypeExtension<GatedSecrets>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

    [ExtendObjectType("RootQuery")]
    [TraxAuthorize]
    public sealed class GatedSecrets
    {
        public string SecretFromMethod() => "method-secret";

        public string SecretFromProperty => "property-secret";
    }
}
