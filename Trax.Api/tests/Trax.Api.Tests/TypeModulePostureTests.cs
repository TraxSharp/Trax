using System.Text;
using AwesomeAssertions;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
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
/// A type extension reaches the schema through <c>AddTypeExtension</c>, <c>ConfigureSchema</c>, or a
/// consumer <c>AddTypeModule</c>. Its <c>[TraxAuthorize]</c> is enforced, and its missing posture
/// refused, however it arrived (api/0003).
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class TypeModulePostureTests
{
    [Test]
    public async Task TraxAuthorizeOnAFieldATypeModuleContributes_RefusesAnAnonymousCaller()
    {
        using var host = await StartAsync(g => g.AddTypeModule<GatedModule>());

        (await PostAsync(host, "{ moduleSecret }"))
            .Should()
            .Contain("TRAX_AUTHORIZATION")
            .And.NotContain("module-secret");
    }

    [Test]
    public async Task AnUndeclaredFieldATypeModuleContributesToTheRoot_FailsStartup()
    {
        var start = () => StartAsync(g => g.AddTypeModule<UndeclaredModule>());

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>(
                "a root field that declares no posture fails startup however it was added"
            );
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

    private static Task<IHost> StartAsync(Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxApiKeyAuth(keys => keys.Add("module-key", id: "reader"));
                        s.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());
                        s.AddTraxGraphQL(g =>
                            configure(g.ExposeOperationQueries().AllowAnonymousOperations())
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

    public sealed class GatedModule : TypeModule
    {
        public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
            IDescriptorContext context,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>([
                new ObjectTypeExtension<GatedModuleFields>(d => d.Name("RootQuery")),
            ]);
    }

    public sealed class GatedModuleFields
    {
        [TraxAuthorize(Roles = "Admin")]
        public string ModuleSecret() => "module-secret";
    }

    public sealed class UndeclaredModule : TypeModule
    {
        public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
            IDescriptorContext context,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>([
                new ObjectTypeExtension<UndeclaredModuleFields>(d => d.Name("RootQuery")),
            ]);
    }

    public sealed class UndeclaredModuleFields
    {
        public string ModuleUndeclared() => "open";
    }
}
