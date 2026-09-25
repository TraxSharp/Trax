using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLBroadcasterIntegrationTests
{
    /// <summary>
    /// The handlers are registered whether a receiver is present or not, because whether one is
    /// present <i>yet</i> depends only on where the caller is in their own startup code.
    /// </summary>
    /// <remarks>
    /// This used to assert the opposite. Gating the registration on the receiver already being in
    /// the collection meant a host calling <c>UseBroadcaster()</c> after <c>AddTraxGraphQL()</c>
    /// lost remote lifecycle events and data-change signals with nothing said about it. Registering
    /// unconditionally costs two descriptors that are never resolved without a receiver, since
    /// <c>TrainEventReceiverService</c> is the only thing that asks for <c>ITrainEventHandler</c>.
    /// </remarks>
    [Test]
    public void AddTraxGraphQL_WithoutReceiver_StillRegistersHandlers()
    {
        var services = new ServiceCollection();

        // Simulate AddTrax() having been called (TraxMarker)
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();

        // Simulate minimal required services for AddTraxGraphQL
        SimulateMinimalTraxServices(services);

        services.AddTraxGraphQLForTesting();

        var handlers = services.Where(sd => sd.ServiceType == typeof(ITrainEventHandler)).ToList();

        handlers
            .Should()
            .HaveCount(
                2,
                "a receiver registered after AddTraxGraphQL must still find its handlers, and "
                    + "nothing resolves them until a receiver exists"
            );
    }

    /// <summary>
    /// The case the old gate got wrong: the receiver arrives after <c>AddTraxGraphQL</c>.
    /// </summary>
    [Test]
    public void AddTraxGraphQL_WithReceiverRegisteredAfterwards_StillRegistersHandlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        SimulateMinimalTraxServices(services);

        services.AddTraxGraphQLForTesting();

        // UseBroadcaster() lands here — after the registration that used to read the collection.
        services.AddSingleton<ITrainEventReceiver>(
            NSubstitute.Substitute.For<ITrainEventReceiver>()
        );

        var handlers = services.Where(sd => sd.ServiceType == typeof(ITrainEventHandler)).ToList();

        handlers
            .Should()
            .Contain(sd =>
                sd.ImplementationType == typeof(Trax.Api.GraphQL.Hooks.GraphQLTrainEventHandler)
            )
            .And.Contain(sd =>
                sd.ImplementationType == typeof(Trax.Api.GraphQL.Hooks.GraphQLDataChangeHandler)
            );
    }

    [Test]
    public void AddTraxGraphQL_WithReceiver_RegistersGraphQLHandler()
    {
        var services = new ServiceCollection();

        // Simulate AddTrax() having been called (TraxMarker)
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();

        // Register a mock receiver (simulating UseBroadcaster having been called)
        services.AddSingleton<ITrainEventReceiver>(
            NSubstitute.Substitute.For<ITrainEventReceiver>()
        );

        SimulateMinimalTraxServices(services);

        services.AddTraxGraphQLForTesting();

        var handlers = services.Where(sd => sd.ServiceType == typeof(ITrainEventHandler)).ToList();

        handlers.Should().NotBeEmpty();
        handlers
            .Should()
            .Contain(sd =>
                sd.ImplementationType == typeof(Trax.Api.GraphQL.Hooks.GraphQLTrainEventHandler)
            );
    }

    private static void SimulateMinimalTraxServices(IServiceCollection services)
    {
        // Register minimal services needed by AddTraxGraphQL
        services.AddSingleton(
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>()
        );
        services.AddSingleton(
            NSubstitute.Substitute.For<Trax.Effect.Services.EffectRegistry.IEffectRegistry>()
        );
    }
}

/// <summary>
/// Extension to call AddTraxGraphQL without full setup for isolated testing of registration logic.
/// </summary>
internal static class TestGraphQLExtensions
{
    internal static IServiceCollection AddTraxGraphQLForTesting(this IServiceCollection services)
    {
        // Call the real extension method. The operations namespace is opt-in;
        // these registration tests do not care about schema shape, but RootQuery
        // would otherwise be empty and AddTraxGraphQL would refuse the build.
        return Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
    }
}
