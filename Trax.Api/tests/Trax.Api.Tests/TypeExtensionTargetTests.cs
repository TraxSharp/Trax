using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
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
/// A type extension whose target type is not in the schema refuses the host at startup, naming
/// the class and the target, rather than leaving the fields it adds silently missing.
/// </summary>
[TestFixture]
public class TypeExtensionTargetTests
{
    [Test]
    public async Task An_extension_of_Subscription_refuses_startup_naming_LifecycleSubscriptions()
    {
        var act = () => StartAsync(g => g.AddTypeExtension<OnDefaultSubscriptionName>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(nameof(OnDefaultSubscriptionName))
            .And.Contain("'Subscription'")
            .And.Contain("LifecycleSubscriptions");
    }

    [Test]
    public async Task An_extension_of_a_name_no_type_has_refuses_startup()
    {
        var act = () => StartAsync(g => g.AddTypeExtension<OnMissingType>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(nameof(OnMissingType))
            .And.Contain("'NoSuchType'");
    }

    [Test]
    public async Task An_extension_of_a_runtime_type_not_in_the_schema_refuses_startup()
    {
        var act = () => StartAsync(g => g.AddTypeExtension<OnMissingRuntimeType>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(nameof(OnMissingRuntimeType))
            .And.Contain(nameof(NotInTheSchema));
    }

    [Test]
    public async Task An_extension_of_RootMutation_on_a_schema_with_no_mutations_refuses_startup()
    {
        var act = () => StartAsync(g => g.AddTypeExtension<OnRootMutation>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(nameof(OnRootMutation))
            .And.Contain("only when the schema exposes a mutation");
    }

    [Test]
    public async Task Extensions_of_types_that_exist_start()
    {
        var act = async () =>
        {
            using var host = await StartAsync(g =>
                g.AddTypeExtension<OnRootQuery>().AddTypeExtension<OnLifecycleSubscriptions>()
            );
        };

        await act.Should().NotThrowAsync();
    }

    private static Task<IHost> StartAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> extensions
    ) =>
        new HostBuilder()
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
                        s.AddTraxGraphQL(g =>
                            extensions(g.ExposeOperationQueries().AllowAnonymousOperations())
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

    [ExtendObjectType(OperationTypeNames.Subscription)]
    public sealed class OnDefaultSubscriptionName
    {
        [Subscribe(With = nameof(SubscribeAsync))]
        [TraxAllowAnonymous]
        public string OnChatEvent([EventMessage] string message) => message;

        public async IAsyncEnumerable<string> SubscribeAsync()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [ExtendObjectType("NoSuchType")]
    public sealed class OnMissingType
    {
        [TraxAllowAnonymous]
        public string Extra() => "extra";
    }

    public sealed class NotInTheSchema
    {
        public string Name { get; set; } = "";
    }

    [ExtendObjectType<NotInTheSchema>]
    public sealed class OnMissingRuntimeType
    {
        [TraxAllowAnonymous]
        public string Extra() => "extra";
    }

    [ExtendObjectType("RootQuery")]
    public sealed class OnRootQuery
    {
        [TraxAllowAnonymous]
        public string Extra() => "extra";
    }

    [ExtendObjectType("RootMutation")]
    public sealed class OnRootMutation
    {
        [TraxAllowAnonymous]
        public bool Extra() => true;
    }

    [ExtendObjectType(nameof(LifecycleSubscriptions))]
    public sealed class OnLifecycleSubscriptions
    {
        [Subscribe(With = nameof(SubscribeAsync))]
        [TraxAllowAnonymous]
        public string OnExtraEvent([EventMessage] string message) => message;

        public async IAsyncEnumerable<string> SubscribeAsync()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
