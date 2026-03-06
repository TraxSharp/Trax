using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLBroadcasterIntegrationTests
{
    [Test]
    public void AddTraxGraphQL_WithoutReceiver_DoesNotRegisterHandler()
    {
        var services = new ServiceCollection();

        // Simulate AddTrax() having been called (TraxMarker)
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();

        // Simulate minimal required services for AddTraxGraphQL
        SimulateMinimalTraxServices(services);

        services.AddTraxGraphQLForTesting();

        var handlers = services.Where(sd => sd.ServiceType == typeof(ITrainEventHandler)).ToList();

        handlers.Should().BeEmpty();
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
        // Call the real extension method
        return Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(services);
    }
}
